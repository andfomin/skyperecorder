using SKYPE4COMLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RecorderCore
{
    public class Skype4ComHelper
    {
        public Skype Skype { get; set; }

        public Skype4ComHelper()
        {
            Skype = new Skype();
        }

    }
}
