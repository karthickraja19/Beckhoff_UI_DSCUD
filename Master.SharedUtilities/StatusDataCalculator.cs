using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.SharedUtilities
{
    public static class StatusDataCalculator
    {
        public static double GetAvailability(double runTime, double availableTime)
        {
            if (availableTime <= 0 || runTime < 0)
                return 0;

            return ((double)runTime /availableTime)*100;
        }

        public static double GetPerformace(int partsProduced, double runTime, double expectedCycleTime)
        {
            if (runTime <= 0 || partsProduced < 0 || expectedCycleTime <= 0)
                return 0;

            return (((double)partsProduced * expectedCycleTime)/ runTime) * 100;
        }

        public static double GetQuality(int unitsSucccedinFirstAttempt, int unitsAttempted)
        {
            if (unitsAttempted <= 0 || unitsSucccedinFirstAttempt < 0)
                return 0;

            return ((double)unitsSucccedinFirstAttempt / unitsAttempted) * 100;
        }

        public static double GetOEE(double availability, double performace, double quality)
        {
            if (availability < 0 || performace < 0 || quality < 0)
                return 0;

            return ((double)(availability/100) * (quality/100) * (performace/100)) * 100;
        }

        public static double GetYield(int unitsSucceded, int unitsAttempted)
        {
            if (unitsSucceded < 0 || unitsAttempted <= 0)
                return 0;

            return ((double)unitsSucceded / unitsAttempted) * 100;
        }

        public static double GetMTBF(double runtime, int alarmsCount)
        {
            if(runtime < 0 || alarmsCount <= 0)
                 return 0;

            return ((double)runtime / alarmsCount);
        }

        public static double GetMTBA(double runtime, int stopsCount)
        {
            if(runtime < 0 || stopsCount <= 0)
                return 0;

            return ((double)runtime / stopsCount);
        }

        public static double GetTossingRate(int unitsFailed, int unitsAttempted)
        {
            if(unitsFailed < 0 || unitsAttempted <= 0)
                return 0;

            return ((double)unitsFailed / unitsAttempted) * 100;
        }

        public static double GetRetestRate(int unitsRetested, int unitsAttempted)
        {
            if(unitsRetested < 0 || unitsAttempted <= 0)
                return 0;

            return ((double)unitsRetested / unitsAttempted) * 100;
        }
    }
}
