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
    public class DeleteOEEDataJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {            
            GlobalClass.DataAccess?.DeleteOEEDataBefore(DateTime.Now.Date);

            return Task.CompletedTask;
        }
    }
}
