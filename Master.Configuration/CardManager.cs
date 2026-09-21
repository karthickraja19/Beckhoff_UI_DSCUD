using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Teal.Library.Contracts;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    public class CardManager
    {
        private TwincatCard _card;
        string configPath = Path.Combine(Application.StartupPath, "Data", "CardsConfiguration.json");
        public CardManager()
        {
            LoadCard();
        }
        public TwincatCard Card => _card;

        public void LoadCard()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"Card Configuration file not found: {configPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string jsonContent = File.ReadAllText(configPath);
                CardConfiguration configFile = JsonConvert.DeserializeObject<CardConfiguration>(jsonContent);
                if (configFile == null)
                {
                    MessageBox.Show($"Failed to deserialize card configuration from file: {configPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                InitializeCards(configFile);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "CardManager", "", "LoadCard", ex.Message);
            }
        }
        public void InitializeCards(CardConfiguration cardConfigs)
        {
            try
            {
                if (cardConfigs == null)
                {
                    MessageBox.Show("No card configurations provided", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                CardProperties cardproperties = new CardProperties()
                {
                    ConnectionId = cardConfigs.ConnectionId,
                    DBName = cardConfigs.DBName,
                    PortNo = cardConfigs.PortNo,
                    ServoDUTName = cardConfigs.ServoDUTName,
                    StepperDUTName = cardConfigs.StepperDUTName,
                    VacuumDUTName = cardConfigs.VacuumDUTName,
                    CylinderDUTName = cardConfigs.CylinderDUTName,
                    InputCount = cardConfigs.InputCount,
                    OutputCount = cardConfigs.OutputCount,
                };

                _card = new TwincatCard(cardproperties);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "CardManager", "", "InitializeCards", ex.Message);
            }
        }
    }
}

