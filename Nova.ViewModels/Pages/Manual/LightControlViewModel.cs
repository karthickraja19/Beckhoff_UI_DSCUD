using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "Light Control" tab — an OPT lighting controller (V3: cmbCOMPort connect,
    /// cmbOPTSend colour recipe, Write_OPT of 4 channel values). The selector picks a
    /// colour recipe whose channel values load into the editor; Connect opens the
    /// serial controller and Trigger = APPLY writes the values via
    /// <see cref="ILightController"/>. Colour recipes are standard, so they stay
    /// seeded here rather than config-driven.
    /// </summary>
    public class LightControlViewModel : ConsoleDeviceViewModel
    {
        private readonly Dictionary<string, ILightController> _controllers =
            new Dictionary<string, ILightController>();
        private readonly Dictionary<string, string> _recipeValues =
            new Dictionary<string, string>();

        public override string SelectorCaption => "Select Light";
        public override string SendCaption => "CHANNEL VALUES";
        public override string DeviceCaption => "SELECTED LIGHT";
        public override string TriggerLabel => "APPLY";
        public override string TriggerIcon => "Lightbulb";

        // V3 has two combos on this tab: cmbCOMPort picks the light object and
        // cmbOPTSend picks the colour code. The primary selector is the object;
        // the colour list is the secondary one.
        public override bool HasSecondarySelector => Colours.Count > 0;
        public override string SecondarySelectorCaption => "Select Colour";

        public ObservableCollection<string> Colours { get; } = new ObservableCollection<string>();

        private string _selectedColour;
        public string SelectedColour
        {
            get => _selectedColour;
            set
            {
                if (SetProperty(ref _selectedColour, value))
                    ApplyRecipe(value);
            }
        }

        /// <summary>The light object currently selected, by name.</summary>
        private ILightController Controller
        {
            get
            {
                ILightController controller;
                var key = SelectedDevice?.Name;
                return key != null && _controllers.TryGetValue(key, out controller) ? controller : null;
            }
        }

        public LightControlViewModel(ILightService lightService)
        {
            // Primary selector: the configured light objects, named by COM port —
            // V3's cmbCOMPort bound to ObjectList.COMObj with DisplayMember OPT_COM.
            var controllers = lightService?.GetControllers();
            if (controllers != null)
            {
                foreach (var controller in controllers)
                {
                    _controllers[controller.Name] = controller;
                    Devices.Add(new ConsoleDeviceItem(controller.Name, "", ""));
                }
            }

            // Secondary selector: colour codes from Data\LightControl_Param.csv, the
            // same source V3 loads into lightcontrol_recipe. Not hardcoded — a machine
            // with different recipes gets its own list.
            var recipes = lightService?.GetRecipes();
            if (recipes != null)
            {
                foreach (var recipe in recipes)
                {
                    _recipeValues[recipe.Name] = recipe.ChannelValues;
                    Colours.Add(recipe.Name);
                }
            }

            if (Devices.Count > 0)
                SelectedDevice = Devices[0];
            if (Colours.Count > 0)
                SelectedColour = Colours[0];

            if (_controllers.Count == 0)
            {
                // Dev fallback (no controller): demo console.
                IsConnected = true;
                Console.Add(new CameraConsoleLine("OPT_CONNECT..."));
                Console.Add(new CameraConsoleLine("ACK CONTROLLER_READY", isAck: true));
            }
        }

        /// <summary>Loads the selected colour's channel values into the editable field.</summary>
        private void ApplyRecipe(string colour)
        {
            string values;
            if (colour != null && _recipeValues.TryGetValue(colour, out values))
                SendCommand = values;
        }

        protected override async Task<bool> DoConnectAsync()
        {
            var controller = Controller;
            return controller == null ? true : await controller.ConnectAsync();
        }

        protected override async Task<string> DoTriggerAsync()
        {
            var controller = Controller;
            if (controller == null)
                return null; // fall back to fake
            bool ok = await controller.ApplyAsync(SendCommand);
            return ok ? "OPT_UPDATED [" + SendCommand + "]" : "";
        }

        protected override Task DoDisconnectAsync()
        {
            Controller?.Disconnect();
            return Task.CompletedTask;
        }

        protected override void OnTrigger()
        {
            Console.Add(new CameraConsoleLine("WRITE_OPT [" + SendCommand + "]"));
            Console.Add(new CameraConsoleLine("ACK OPT_UPDATED", isAck: true));
        }

        
    }
}
