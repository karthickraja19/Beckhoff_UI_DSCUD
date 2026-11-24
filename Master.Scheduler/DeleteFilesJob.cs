using Quartz;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Scheduler
{
    public class DeleteFilesJob : IJob   //delete before 30 days csv files
    {
        
        public Task Execute(IJobExecutionContext context)
        {
            string folderPath = ConfigurationManager.AppSettings["LogFilePath"];

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine($"CSV folder not found: {folderPath}");
                return Task.CompletedTask;
            }

            foreach (var file in Directory.GetFiles(folderPath, "*.csv", SearchOption.AllDirectories))
            {
                try
                {
                    DateTime currenttime = DateTime.Now.Date;
                    var retention_date = currenttime.AddDays(-(int.Parse(ConfigurationManager.AppSettings["RetentionDaysForCSV"])));
                    DateTime lastModified = File.GetLastWriteTime(file);
                    if (lastModified <= retention_date)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting {file}: {ex.Message}");
                }
            }
            return Task.CompletedTask;
        }
    }
}
