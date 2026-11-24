using Master.Configuration.Model;
using System;
using System.IO;
using System.Windows.Forms;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The Beckhoff card (the PLC's ADS connection) described by CardsConfiguration.json. Its
    /// <see cref="Card"/> is null when the file is missing or is not a Beckhoff card
    /// configuration; <see cref="MachineConfiguration.Load"/> reports why.
    /// </summary>
    public class CardManager
    {
        private TwincatCard _card;

        public CardManager()
        {
            LoadCard();
        }

        public TwincatCard Card => _card;

        /// <summary>
        /// Builds the card from the configuration already loaded; reads the Data folder beside
        /// the application first if nothing has been loaded yet.
        /// </summary>
        public void LoadCard()
        {
            try
            {
                if (!MachineConfiguration.IsLoaded)
                {
                    foreach (string problem in MachineConfiguration.Load(Path.Combine(Application.StartupPath, "Data")))
                        Logger.WriteLog(LogType.Error, "CardManager", "LoadCard", problem, "");
                }

                InitializeCards(ConfigGlobalClass.CardConfiguration);
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
                    Logger.WriteLog(LogType.Error, "CardManager", "InitializeCards", "No card configuration",
                        "CardsConfiguration.json is missing or is not a Beckhoff card configuration.");
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
                    InputArray = cardConfigs.InputArray,
                    OutputArray = cardConfigs.OutputArray,
                    IOFirstIndex = cardConfigs.IOFirstIndex,
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
