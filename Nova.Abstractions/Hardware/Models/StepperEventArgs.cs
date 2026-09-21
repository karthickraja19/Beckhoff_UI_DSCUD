using System;
namespace Nova.Abstractions.Hardware
{
    public class StepperEventArgs : EventArgs
    {
        public string ComponentId { get; set; }
        public StepperEventArgs(string componentId) { ComponentId = componentId; }
    }
}
