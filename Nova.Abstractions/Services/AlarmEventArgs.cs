using System;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Services
{
    public class AlarmEventArgs : EventArgs
    {
        public ECode ErrorCode { get; set; }
        public string ErrorCodeString { get; set; }
        public string ErrorDescription { get; set; }
        public string ErrorName { get; set; }
        public string ErrorContent { get; set; }
        public DateTime OccurredTime { get; set; }
        public bool IsActive { get; set; }
    }
}
