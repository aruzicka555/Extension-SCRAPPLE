﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Landis.Library.Climate;
using Landis.Core;

namespace Landis.Extension.Scrapple
{
// This class calculates fire weather variables. These calculations are based on the Canadian Fire and Fuels System.
// Refer to Lawson and Armitage 2008 "Weather Guide for the Canadian Forest Fire Danger Rating System" for further explanation
//This comment is to further test the GitHub commit process

    public class AnnualFireWeather
    {
         
        public double[] FireWeatherIndex = new double[365];

        //public static double  FineFuelMoistureCode;      

        //public static double DuffMoistureCode;   

        //public static double DroughtCode;  
            
        //public static double BuildUpIndex;                
            
        public double[] WindSpeedVelocity;          
            
        public double[] WindAzimuth;              

        //public Season mySeason;    
            
        // public int Ecoregion;          //RMS:  Necessary?        // VS: I don't believe it is
        private int actualYear;

        public AnnualFireWeather(int actualYear)
        {
            this.actualYear = actualYear;
            FireWeatherIndex = AnnualClimate.IsLeapYear(actualYear) ? new double[365] : new double[364];
            WindSpeedVelocity = AnnualClimate.IsLeapYear(actualYear) ? new double[365] : new double[364];
            WindAzimuth = AnnualClimate.IsLeapYear(actualYear) ? new double[365] : new double[364];
        }

        //public enum Season {Winter, Spring, Summer, Fall};

        // VS: pretty sure this is just for yearly so day is probably not necessary TBD
        public void CalculateAnnualFireWeather(/*int day,*/ IEcoregion ecoregion)
        {
            double RHslopeAdjust =  PlugIn.RelativeHumiditySlopeAdjust;
            
            for (int d = 0; d <= 365; d++) //This section loops through all the days of the fire season and retrieves various climate variables below
            {
                AnnualClimate_Daily myWeatherData;  
                double temperature = -9999.0;
                double precipitation = -9999.0;
                WindSpeedVelocity[d] = -9999.0;
                WindAzimuth[d] = -9999.0;
                double relative_humidity = -9999;

                int actualYear = (PlugIn.ModelCore.CurrentTime -1) + Climate.Future_DailyData.First().Key;  

                if (Climate.Future_DailyData.ContainsKey(actualYear))
                {
                    //what is annualAET
                    double test = Climate.Future_DailyData[actualYear][ecoregion.Index].AnnualAET;

                    myWeatherData = Climate.Future_DailyData[actualYear][ecoregion.Index];
                    temperature = (myWeatherData.DailyMaxTemp[d] + myWeatherData.DailyMinTemp[d]) / 2;  
                    precipitation = myWeatherData.DailyPrecip[d];
                    WindSpeedVelocity[d] = myWeatherData.DailyWindSpeed[d];
                    WindAzimuth[d] = myWeatherData.DailyWindDirection[d];
                    relative_humidity = 100 * Math.Exp((RHslopeAdjust * myWeatherData.DailyMinTemp[d]) / (273.15 + myWeatherData.DailyMinTemp[d]) - (RHslopeAdjust * temperature) / (273.15 + temperature));
                    //Relative humidity calculations include RHslopeadjust variable to correct for location of study.
                    myWeatherData.DailyFireWeatherIndex[d] = CalculateFWI(precipitation, temperature);
                    FireWeatherIndex[d] = myWeatherData.DailyFireWeatherIndex[d];
                }
                else
                {
                    PlugIn.ModelCore.UI.WriteLine("Cannot find fire weather data for {0} for year {1}.", ecoregion.Name, PlugIn.ModelCore.CurrentTime);
                }

            } 

           return;
    }

        private static double CalculateFWI(double precipitation, double temperature)
        {
            double FWI = 0.0;

            return FWI;
        }

        private static int CalculateMonth(int d)
    {
        int month = 0;

        if (d <= 31)
        {
            month = 1;
        }
        else if (d > 31 && d <= 60) 
        {
            month = 2;
        }
        else if (d > 60 && d <= 91) 
        {
            month = 3;
        }
        else if (d > 91 && d <= 121)
        {
            month = 4;
        }
        else if (d > 121 && d <= 152)
        {
            month = 5;
        }
        else if (d > 152 && d <= 182)
        {
            month = 6;
        }
        else if (d > 182 && d <= 213)
        {
            month = 7;
        }
        else if (d > 213 && d <= 244)
        {
            month = 8;
        }
        else if (d > 244 && d <= 274)
        {
            month = 9;
        }
        else if (d > 274 && d <= 305)
        {
            month = 10;
        }
        else if (d > 305 && d <= 335)
        {
            month = 11;
        }

        else 
        {
            month = 12;
        }

        return month;                
    }


    private static double CalculateWindFunctionISI(int d, double WindSpeedVelocity)
	{
		double WindFunction_ISI = 0.0;

        WindFunction_ISI = Math.Exp(0.05039 * WindSpeedVelocity);

		return WindFunction_ISI;
	}


    }
} 
