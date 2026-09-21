using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Hardware
{
    public interface ICard
    {
        int CardIndex { get; }
        bool IsInitiated { get; }
        IntPtr CardHandle { get;  }
        int AxesCount { get; }
        IntPtr[] AxesHandles { get; }
        int DIchannelnum { get; }
        int DOchannelnum { get; }

        event Action<CardEventArgs> Connected;
        event Action<CardEventArgs> Disconnected;
        event Action<CardEventArgs> ConnectFailed;

        CardInitStatus ConnectCard();
        bool DisconnectCard();
        bool OnAllAxes();
        bool OffAllAxes();
        bool ResetAllAxesErrors();
        void StopAllAxesMotion();
        bool WriteDOBit(int OpNum, bool value);
        bool[] RefreshDI();
        bool RefreshAISetup(bool IsInitial);
        double[] RefreshAIvalues();
    }
}

