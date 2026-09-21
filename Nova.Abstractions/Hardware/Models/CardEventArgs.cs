using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class CardEventArgs : EventArgs
    {
        public int CardNo { get; }
        public DateTime Timestamp { get; }

        public CardEventArgs(int cardIndex)
        {
            CardNo = cardIndex;
            Timestamp = DateTime.UtcNow;
        }
    }
}

