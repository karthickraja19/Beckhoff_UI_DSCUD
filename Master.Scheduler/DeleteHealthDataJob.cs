using Master.SharedUtilities;
using Quartz;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Scheduler
{
    public class DeleteHealthDataJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            var retentionDays = int.Parse(ConfigurationManager.AppSettings["RetentionDaysForHealthData"]);

            GlobalClass.DataAccess?.DeleteHealthDataBefore(DateTime.Now.AddDays(-retentionDays));

            return Task.CompletedTask;
        }
    }
}
