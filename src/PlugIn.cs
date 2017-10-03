//  Authors:  Robert M. Scheller, Alec Kretchun, Vincent Schuster

using Landis.Library.AgeOnlyCohorts;
using Landis.SpatialModeling;
using Landis.Library.Climate;
using Landis.Core;
using Landis.Library.Metadata;

using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using System.Reflection;
using System.Linq;
using System.Diagnostics;

namespace Landis.Extension.Scrapple
{
    ///<summary>
    /// A disturbance plug-in that simulates Fire disturbance.
    /// </summary>
    public class PlugIn
        : ExtensionMain 
    {
        private static readonly bool isDebugEnabled = true; 

        public static readonly ExtensionType ExtType = new ExtensionType("disturbance:fire");
        public static readonly string ExtensionName = "SCRAPPLE";
        public static MetadataTable<EventsLog> eventLog;
        public static MetadataTable<SummaryLog> summaryLog;
        public static MetadataTable<IgnitionsLog> ignitionsLog;
        
        // Get the active sites from the landscape and shuffle them 
        public List<ActiveSite> activeRxSites; 
        public List<ActiveSite> activeAccidentalSites;
        public List<ActiveSite> activeLightningSites;

        public static int FutureClimateBaseYear;

        private static int totalBurnedSites;
        private static int numberOfFire;
        private static int totalBiomassMortality;
        private static int numCellsSeverity1;
        private static int numCellsSeverity2;
        private static int numCellsSeverity3;

        public static IInputParameters Parameters;
        private static ICore modelCore;

        public static double MaximumSpreadAreaB0;
        public static double MaximumSpreadAreaB1;
        public static double MaximumSpreadAreaB2;

        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName, ExtType)
        {
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }
        
        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile,
                                            ICore mCore)
        {
            modelCore = mCore;
            SiteVars.Initialize();
            InputParameterParser parser = new InputParameterParser();
            Parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
        }

        //---------------------------------------------------------------------

        public override void Initialize()
        {
            Timestep = 1;  // RMS:  Initially we will force annual time step. parameters.Timestep;

            // Later, if maps are dynamic, this process will need to be repeated every time the maps are updated.
            activeAccidentalSites = new List<ActiveSite>();
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
                //if (SiteVars.AccidentalFireWeight[site] > 0.0)
                    activeAccidentalSites.Add(site);

            activeRxSites = new List<ActiveSite>();
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
                //if (SiteVars.RxFireWeight[site] > 0.0)
                    activeRxSites.Add(site);

            activeLightningSites = new List<ActiveSite>();
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
                //if (SiteVars.LightningFireWeight[site] > 0.0)
                    activeLightningSites.Add(site);

            ///******************** DEBUGGER LAUNCH *********************
            /// 
            /*
            if (Debugger.Launch())
            {
                modelCore.UI.WriteLine("Debugger is attached");
                if (Debugger.IsLogging())
                {
                    modelCore.UI.WriteLine("Debugging is logging");
                }
                Debugger.Break();
            }
            else
            { 
                modelCore.UI.WriteLine("Debugger not attached");
            }
            */
            ///******************** DEBUGGER END *********************

            // Initilize the FireRegions Maps
            modelCore.UI.WriteLine("   Initializing Fire...");
            MapUtility.Initilize(Parameters.LighteningFireMap, Parameters.AccidentalFireMap, Parameters.RxFireMap,
                                 Parameters.LighteningSuppressionMap, Parameters.AccidentalSuppressionMap, Parameters.RxSuppressionMap);
            MetadataHandler.InitializeMetadata(Parameters.Timestep, ModelCore);

            //FireEvent.Initialize(parameters.FireDamages);

            //if (isDebugEnabled)
            //    modelCore.UI.WriteLine("Initialization done");
        }

        


        //---------------------------------------------------------------------

        ///<summary>
        /// Run the plug-in at a particular timestep.
        ///</summary>
        public override void Run()
        {

            if (PlugIn.ModelCore.CurrentTime > 0)
                SiteVars.InitializeDisturbances();

            SiteVars.Disturbed.ActiveSiteValues = false;
            AnnualClimate_Daily weatherData = null;
            totalBurnedSites = 0;
            numberOfFire = 0;
            totalBiomassMortality = 0;
            numCellsSeverity1 = 0;
            numCellsSeverity2 = 0;
            numCellsSeverity3 = 0;

            modelCore.UI.WriteLine("   Processing landscape for Fire events ...");

            int actualYear = 0;
            try
            {
                actualYear = (PlugIn.ModelCore.CurrentTime - 1) + Climate.Future_AllData.First().Key;
            }
            catch
            {
                throw new UninitializedClimateData("Could not initilize the actual year from climate data");
            }
            
           
            int daysPerYear = 366;
            // VS: hasn't been properly integrated into Climate Library.
            //int daysPerYear = (AnnualClimate.IsLeapYear(actualYear) ? true : false) ? 366 : 365;


            // Get the active sites from the landscape and shuffle them 
            // Potential area for future optimization
            //List<ActiveSite> activeSites = PlugIn.ModelCore.Landscape.ToList();

            // Sites are weighted for ignition in the Shuffle method, based on the respective inputs maps.
            List<ActiveSite> shuffledAccidentalFireSites = Shuffle(activeAccidentalSites, SiteVars.AccidentalFireWeight);
            List<ActiveSite> shuffledLightningFireSites = Shuffle(activeLightningSites, SiteVars.LightningFireWeight);
            List<ActiveSite> shuffledRxFireSites = Shuffle(activeRxSites, SiteVars.RxFireWeight);

            int numRxFires = Parameters.NumberRxAnnualFires;
            for (int day = 0; day < daysPerYear; ++day)
            {
                double landscapeAverageFireWeatherIndex = 0.0;
                // number of fires get initilized to 0 every timestep
                int numFires = 0;

                foreach (IEcoregion ecoregion in PlugIn.ModelCore.Ecoregions)
                {
                    if (!ecoregion.Active)
                        continue;

                    try
                    {
                        weatherData = Climate.Future_DailyData[actualYear][ecoregion.Index];
                    }
                    catch
                    {
                        throw new UninitializedClimateData(string.Format("Climate data could not be found \t year: {0} in ecoregion: {1}", actualYear, ecoregion.Name));
                    }

                    try
                    {
                        landscapeAverageFireWeatherIndex += weatherData.DailyFireWeatherIndex[day];
                    }
                    catch
                    {
                        throw new UninitializedClimateData(string.Format("Fire Weather Index could not be found \t year: {0}, day: {1} in ecoregion: {2} not found", actualYear, day, ecoregion.Name));
                    }
                }

                landscapeAverageFireWeatherIndex /= ModelCore.Ecoregions.Count;  // THIS IS NOT CORRECTLY WEIGHTED - ALSO DOES NOT DISCOUNT INACTIVE
                // FWI must be > .10 
                if (landscapeAverageFireWeatherIndex >= 0.10)
                {
                    //List<ActiveSite> shuffledAccidentalFireSites = Shuffle(ModelCore.Landscape.ActiveSites.ToList(), SiteVars.AccidentalFireWeight);
                    numFires = NumberOfIgnitions(Ignition.Accidental, landscapeAverageFireWeatherIndex);

                    // Ignite Accidental Fires.
                    for (int i = 0; i < numFires; ++i)
                    {
                        Ignite(Ignition.Accidental, shuffledAccidentalFireSites, day, landscapeAverageFireWeatherIndex);
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, Ignition.Accidental.ToString(), numFires, day);
                    }

                    // Ignite Lightning Fires
                    //List<ActiveSite> shuffledLightningFireSites = Shuffle(ModelCore.Landscape.ActiveSites.ToList(), SiteVars.LightningFireWeight);
                    numFires = NumberOfIgnitions(Ignition.Lightning, landscapeAverageFireWeatherIndex);
                    for (int i = 0; i < numFires; ++i)
                    {
                        Ignite(Ignition.Lightning, shuffledLightningFireSites, day, landscapeAverageFireWeatherIndex);
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, Ignition.Lightning.ToString(), numFires, day);
                    }

                    //List<ActiveSite> shuffledRxFireSites = Shuffle(ModelCore.Landscape.ActiveSites.ToList(), SiteVars.RxFireWeight);
                    // Ignite a single Rx fire per day
                    if (numRxFires > 0 &&
                        landscapeAverageFireWeatherIndex > Parameters.MinRxFireWeatherIndex &&
                        landscapeAverageFireWeatherIndex < Parameters.MaxRxFireWeatherIndex &&
                        weatherData.DailyWindSpeed[day] < Parameters.MaxRxWindSpeed)
                    {
                        Ignite(Ignition.Rx, shuffledRxFireSites, day, landscapeAverageFireWeatherIndex);
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, Ignition.Rx.ToString(), 1, day);
                        numRxFires--;
                    }
                }
            }

            WriteMaps(PlugIn.ModelCore.CurrentTime);

            WriteSummaryLog(modelCore.CurrentTime);

            if (isDebugEnabled)
                modelCore.UI.WriteLine("Done running extension");
        }


        //---------------------------------------------------------------------

        private void WriteMaps(int currentTime)
        {
            string path = MapNames.ReplaceTemplateVars("fire/ignition-type-{timestep}.img", currentTime);

            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path, modelCore.Landscape.Dimensions))
            {
                ShortPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site])
                            pixel.MapCode.Value = (short) (SiteVars.TypeOfIginition[site] + 1);
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            path = MapNames.ReplaceTemplateVars("fire/severity-{timestep}.img", currentTime);
            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path, modelCore.Landscape.Dimensions))
            {
                ShortPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site])
                            pixel.MapCode.Value = (short) (SiteVars.Severity[site]);
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            path = MapNames.ReplaceTemplateVars("fire/day-of-fire-{timestep}.img", currentTime);
            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path, modelCore.Landscape.Dimensions))
            {
                ShortPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site])
                            pixel.MapCode.Value = (short) (SiteVars.DayOfFire[site]);
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }
        }


        //---------------------------------------------------------------------
        // The random selection based on input map weights
        public static ActiveSite SelectRandomSite(List<ActiveSite> list, ISiteVar<double> weightedSiteVar, double totalWeight)
        {
            ActiveSite selectedSite = list.FirstOrDefault(); // currently selected element

            int randomNum = FireEvent.rnd.Next((int)totalWeight);
            while (randomNum > totalWeight)
            {
                randomNum = FireEvent.rnd.Next(list.Count);
            }
            
            //check to make sure it is 
            foreach (ActiveSite site in list)
            {
                if (randomNum < weightedSiteVar[site] )
                {
                    selectedSite = site;
                    break;
                }

                randomNum -= (int)weightedSiteVar[site];
            }

            return selectedSite; // when iterations end, selected is some element of sequence. 
        }

        //---------------------------------------------------------------------

        //  Determines the number of Ignitions per day
        //  Returns: 0 <= numIgnitons <= 3
        private static int NumberOfIgnitions(Ignition ignitionType, double fireWeatherIndex)
        {
            double b0 = 0.0;
            double b1 = 0.0;
            if(ignitionType == Ignition.Lightning)
            {
                b0 = Parameters.LightningIgnitionB0;
                b1 = Parameters.LightningIgnitionB1;
            }
            //if (ignitionType == Ignition.Rx)
            //{
            //    b0 = parameters.RxFireIgnitionB0;
            //    b1 = parameters.RxFireIgnitionB1;
            //}
            if (ignitionType == Ignition.Accidental)
            {
                b0 = Parameters.AccidentalFireIgnitionB0;
                b1 = Parameters.AccidentalFireIgnitionB1;
            }

            int numIgnitions = 0;
            double possibleIgnitions = Math.Pow(Math.E, (b0 + (b1* fireWeatherIndex)));
            int floorPossibleIginitions = (int)Math.Floor(possibleIgnitions);
            numIgnitions +=  floorPossibleIginitions;
            numIgnitions += (modelCore.GenerateUniform() <= (possibleIgnitions - (double)floorPossibleIginitions) ? 1 : 0);
            return numIgnitions;
        }

        //---------------------------------------------------------------------

        // Ignites and Spreads a fire
        private static void Ignite(Ignition ignitionType, List<ActiveSite> shuffledFireSites, int day, double fireWeatherIndex)
        {
            //int attempts = 0;
            // TESTING REMOVING THIS AS IT IS VERY SLOW
            //while ( shuffledFireSites.Count() > 0 && SiteVars.Disturbed[shuffledFireSites.First()] == true )
            //{
            //    shuffledFireSites.Remove(shuffledFireSites.First());
            //}
            if (shuffledFireSites.Count() > 0)
            {
                FireEvent fireEvent = FireEvent.Initiate(shuffledFireSites.First(), modelCore.CurrentTime, day, ignitionType);

                if (fireEvent.CohortsKilled > 0)
                {
                    totalBurnedSites = fireEvent.TotalSitesDamaged;
                    numberOfFire++;
                    totalBiomassMortality = (int) fireEvent.TotalBiomassMortality;
                    numCellsSeverity1 = fireEvent.NumberCellsSeverity1;
                    numCellsSeverity2 = fireEvent.NumberCellsSeverity2;
                    numCellsSeverity3 = fireEvent.NumberCellsSeverity3;

                }



                //attempts++;
            }
            //if (attempts > 500)
            //    break;
        }

        //---------------------------------------------------------------------

        // Determines if an Rx Fire will start on the given day
        // RMS TODO
        private bool AllowRxFire(int day, double fireWeatherIndex)
        {
            return false;
        }

        //---------------------------------------------------------------------

        public static void LogIgnition(int currentTime, double fwi, string type, int numIgns, int doy)
        {

            ignitionsLog.Clear();
            IgnitionsLog ign = new IgnitionsLog();
            ign.SimulationYear = currentTime;
            ign.AttemptedNumberIgnitions = numIgns;
            //ign.InitRow = site.Location.Row;
            //ign.InitColumn = site.Location.Column;
            ign.DayOfYear = doy;
            ign.FireWeatherIndex = fwi;
            ign.IgnitionType = type;

            ignitionsLog.AddObject(ign);
            ignitionsLog.WriteToFile();

        }


        //---------------------------------------------------------------------
        // A helper function to shuffle a list of ActiveSties: Algorithm may be improved.
        // Sites are weighted for ignition in the Shuffle method, based on the respective inputs maps.

        private static List<ActiveSite> Shuffle(List<ActiveSite> list, ISiteVar<double> weightedSiteVar)
        {
            List<ActiveSite> shuffledList = new List<ActiveSite>();
            double totalWeight = 0;
            foreach (ActiveSite site in list)
            {
                totalWeight += weightedSiteVar[site];
            }

            while (list.Count > 0)
            {
                ActiveSite toAdd;
                toAdd = SelectRandomSite(list, weightedSiteVar, totalWeight);
                shuffledList.Add(toAdd);
                totalWeight -= weightedSiteVar[toAdd];
                list.Remove(toAdd);
            }

            return shuffledList;
        }

        //---------------------------------------------------------------------

        private void WriteSummaryLog(int currentTime)
        {
            summaryLog.Clear();
            SummaryLog sl = new SummaryLog();
            sl.SimulationYear = currentTime;
            sl.TotalBurnedSites = totalBurnedSites; 
            sl.NumberFires = numberOfFire;
            sl.TotalBiomassMortality = totalBiomassMortality;
            sl.NumberCellsSeverity1 = numCellsSeverity1;
            sl.NumberCellsSeverity2 = numCellsSeverity2;
            sl.NumberCellsSeverity3 = numCellsSeverity3;


            summaryLog.AddObject(sl);
            summaryLog.WriteToFile();
        }

        //---------------------------------------------------------------------

        /*
         * VS:: THIS MAY BE NEEDED
         * 
        private static List<ActiveSite> Sort<T>(List<ActiveSite> list, ISiteVar<T> type)
        {
            List<ActiveSite> sortedList = new List<ActiveSite>();
            foreach(ActiveSite site in list)
            {
                type[site] 
            }
        }
        */

    }
}
