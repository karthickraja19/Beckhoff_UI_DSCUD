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
    public class DeleteProductionDataJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            var retentionDays = int.Parse(ConfigurationManager.AppSettings["RetentionDaysForProductionData"]);

            GlobalClass.DataAccess?.DeleteProductionDataBefore(DateTime.Now.AddDays(-retentionDays));

            return Task.CompletedTask;
        }
    }
}
