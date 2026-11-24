namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One line in the camera "Received Response" console. <see cref="IsAck"/> flags
    /// acknowledgement lines so the view can render them in the success colour.
    /// </summary>
    public class CameraConsoleLine
    {
        public string Text { get; }
        public bool IsAck { get; }

        public CameraConsoleLine(string text, bool isAck = false)
        {
            Text = text;
            IsAck = isAck;
        }
    }
}
