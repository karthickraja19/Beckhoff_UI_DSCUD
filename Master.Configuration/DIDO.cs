using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Teal.Library.Contracts;
using Teal.Library.Log;


namespace Master.Configuration
{
    //Class created for whole Digital Input and Output module
    public class DIDO
    {
        //public static List<IInput> g_lstInput = new List<IInput>();
        //public static List<IOutput> g_lstOutput = new List<IOutput>();

        //public static List<HCAxis> g_lstAXIS = new List<HCAxis>();
        //public static List<IAxis> g_lstIAxis = new List<IAxis>();


        /// <summary>
        /// Load the IO and Axis Configuration from File to the List.
        /// Load the IO parameter from Config.csv file and check the list with the Enum's declared
        /// </summary>
        /// <returns>Returns bool</returns>                
        public bool LoadConfig()
        {
            try
            {
                //AxisLoad();
                List<string[]> ls = new List<string[]>();
                // Resolve next to the exe, not from the working directory. Every other
                // loader here (Servos/Steppers/Cylinders/Vacuums/CCDs/Scanners/Robots/
                // Pdca/CardsConfiguration) uses Application.StartupPath; Config.csv was
                // the only one on Environment.CurrentDirectory, so launching from a
                // shortcut without "Start in" — or after any dialog moved the CWD — left
                // lstInputConfig/lstOutputConfig empty while every other device loaded.
                string configCsvPath = Path.Combine(Application.StartupPath, "Data", "Config.csv");
                using (StreamReader sr = new StreamReader(configCsvPath, System.Text.Encoding.UTF8))
                {
                    string line = string.Empty;
                    while (!sr.EndOfStream)
                    {
                        line = sr.ReadLine();
                        if (line != null && line.Length > 0)
                        {
                            ls.Add(line.Split(','));
                        }
                    }
                }
                List<IOParam> inputTempList = new List<IOParam>();
                List<IOParam> outputTempList = new List<IOParam>();
                List<IOParam> analoginputTempList = new List<IOParam>();


                ConfigGlobalClass.lstInputConfig.Clear();
                ConfigGlobalClass.lstOutputConfig.Clear();
                ConfigGlobalClass.lstAnalogIntputConfig.Clear();


                for (int i = 1; i < ls.Count; i++)
                {
                    if (ls[i].Length >= 6)
                    {
                        if (ls[i][1].ToUpper().ToString() == "INPUT")
                        {
                            IOParam input = new IOParam();
                            input.CardNo = Convert.ToInt16(ls[i][2]);
                            input.ExCardNo = Convert.ToInt16(ls[i][3]);
                            input.Id = Convert.ToInt16(ls[i][4]);
                            input.Name = ls[i][5];
                            if (ls[i].Length >= 7)
                                input.FerruleName = ls[i][6];

                            inputTempList.Add(input);
                        }

                        if (ls[i][1].ToUpper().ToString() == "OUTPUT")
                        {
                            IOParam output = new IOParam();
                            output.CardNo = Convert.ToInt16(ls[i][2]);
                            output.ExCardNo = Convert.ToInt16(ls[i][3]);
                            output.Id = Convert.ToInt16(ls[i][4]);
                            output.Name = ls[i][5];
                            if (ls[i].Length >= 7)
                                output.FerruleName = ls[i][6];
                            // Safe boolean conversion: default to false if empty or missing
                            if (ls[i].Length >= 8 && !string.IsNullOrWhiteSpace(ls[i][7]))
                                output.IsEnabled = Convert.ToBoolean(ls[i][7]);
                            else
                                output.IsEnabled = true;

                            outputTempList.Add(output);
                        }

                        if (ls[i][1].ToUpper().ToString() == "ANALOG_INPUT")
                        {
                            IOParam ANALOG_INPUT = new IOParam();
                            ANALOG_INPUT.CardNo = Convert.ToInt16(ls[i][2]);
                            ANALOG_INPUT.ExCardNo = Convert.ToInt16(ls[i][3]);
                            ANALOG_INPUT.Id = Convert.ToInt16(ls[i][4]);
                            ANALOG_INPUT.Name = ls[i][5];
                            if (ls[i].Length >= 7)
                                ANALOG_INPUT.FerruleName = ls[i][6];

                            analoginputTempList.Add(ANALOG_INPUT);
                        }
                    }
                }

                #region Reorder by project enumeration definition total_IO and total Axis
                bool bFind = false;
                string sLackMsg = "";
                sLackMsg = "";
                ConfigGlobalClass.lstInputConfig.Clear();
                try
                {
                    for (int i = 0; i < ((int)EInput.End); i++)
                    {
                        bFind = false;
                        for (int j = 0; j < inputTempList.Count; j++)
                        {
                            string testi = (EInput.Start + i + 1).ToString();
                            if (inputTempList[j].Name == (EInput.Start + i + 1).ToString())
                            {
                                ConfigGlobalClass.lstInputConfig.Add(inputTempList[j]);
                                bFind = true;
                                break;
                            }
                        }

                        if (!bFind)
                        {
                            sLackMsg += (EInput.Start + i + 1).ToString() + "\n";
                        }
                    }
                }
                catch (Exception ex)
                {

                }
                var data = (int)EInput.End;
                var data1 = ConfigGlobalClass.lstInputConfig.Count;
                if (ConfigGlobalClass.lstInputConfig.Count < ((int)EInput.End))
                {
                    Logger.WriteLog(LogType.Error, "DIO", "", "Input Mismatch", "Total Number of Inputs doesn't match to Specified Inputs   \n" + sLackMsg);
                    MessageBox.Show("Total Number of Inputs doesn't match to Specified Inputs");
                    return false;
                }

                sLackMsg = "";
                ConfigGlobalClass.lstOutputConfig.Clear();
                for (int i = 0; i < ((int)EOutput.End); i++)
                {
                    bFind = false;
                    for (int j = 0; j < outputTempList.Count; j++)
                    {
                        if (outputTempList[j].Name == (EOutput.Start + i + 1).ToString())
                        {
                            ConfigGlobalClass.lstOutputConfig.Add(outputTempList[j]);
                            bFind = true;
                            break;
                        }
                    }
                    if (!bFind)
                    {
                        sLackMsg += (EOutput.Start + i + 1).ToString() + "\n";
                    }
                }
                if (ConfigGlobalClass.lstOutputConfig.Count < ((int)EOutput.End))
                {
                    Logger.WriteLog(LogType.Error, "DIO", "", "Output Mismatch", "Total Number of EOutput doesn't match to Specified Outputs  \n" + sLackMsg);
                    MessageBox.Show("Total Number of EOutput doesn't match to Specified Outputs");
                    return false;
                }
                sLackMsg = "";
                ConfigGlobalClass.lstAnalogIntputConfig.Clear();
                ConfigGlobalClass.Analog_Input_Names = new string[(int)Analog_Input.End];
                for (int i = 0; i < ((int)Analog_Input.End); i++)
                {
                    bFind = false;
                    for (int j = 0; j < analoginputTempList.Count; j++)
                    {
                        if (analoginputTempList[j].Name == (Analog_Input.Start + i + 1).ToString())
                        {
                            Console.WriteLine(analoginputTempList[j].Name + j);
                            ConfigGlobalClass.lstAnalogIntputConfig.Add(analoginputTempList[j]);
                            ConfigGlobalClass.Analog_Input_Names[j] = ConfigGlobalClass.lstAnalogIntputConfig[j].Name;
                            bFind = true;
                            break;
                        }
                    }
                    if (!bFind)
                    {
                        sLackMsg += (Analog_Input.Start + i + 1).ToString() + "\n";
                    }
                }


                // Config.csv on this machine carries no ANALOG_INPUT rows (no load cells wired
                // yet), so this count is always short of Analog_Input.End. The reference AAA
                // implementation (Working_AAA) hit the same gap and disabled this check rather
                // than block startup on it; matching that here so DIDO.LoadConfig() doesn't
                // hard-fail Input/Output resolution over an unrelated, unused signal group.
                //if (ConfigGlobalClass.lstAnalogIntputConfig.Count < ((int)Analog_Input.End))
                //{
                //    Logger.WriteLog(LogType.Error, "AIO", "", "Analog_Input Mismatch", "Total Number of Analog_Input doesn't match to Specified Inputs  \n" + sLackMsg);
                //    MessageBox.Show("Total Number of Analog_Input doesn't match to Specified Analog_Input");

                //    return false;
                //}
                #endregion

                Logger.WriteLog(LogType.Info, "DIO", "", "Success", "Successfully Completed");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "DIO", "", ex.Message, ex.ToString());
                return false;
            }
            return true;
        }

        /// <summary>
        /// To perform the specified operation for all the components either On/Off or Open/Close
        /// </summary>
        /// <param name="value">True/False to perform the required operation</param>
        /// <param name="OpNum">Pass the required enum value</param>
        /// <returns>Returns bool</returns>
        public bool WriteOPS(bool value, int OpNum)
        {
            try
            {
                return ConfigGlobalClass.cardManager.Card.WriteDO(OpNum, value);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "DIO", "", ex.Message, ex.ToString());
                return false;
            }
        }

        public bool ReadDI(int input)
        {
            try
            {
                return ConfigGlobalClass.cardManager.Card.ReadDI(input);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "ReadDI", "", ex.Message, ex.ToString());
                return false;
            }
        }
        public bool ReadDO(int output)
        {
            try
            {
                return ConfigGlobalClass.cardManager.Card.ReadDO(output);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "ReadDO", "", ex.Message, ex.ToString());
                return false;
            }
        }
        
    }
}
