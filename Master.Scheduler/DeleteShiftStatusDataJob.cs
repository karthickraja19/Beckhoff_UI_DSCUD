using Master.SharedUtilities;
using Quartz;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Scheduler
{
    public class DeleteShiftStatusDataJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            var retentionDays = int.Parse(ConfigurationManager.AppSettings["RetentionDaysForShiftStatusData"]);

            GlobalClass.DataAccess?.DeleteShiftStatusDataBefore(DateTime.Now.AddDays(-retentionDays));

            return Task.CompletedTask;
        }
    }

}

