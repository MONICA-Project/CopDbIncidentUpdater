using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CopDbIncidentUpdater
{
    public static class settings
    {
        public static string ConnectionString = "";
        public static string CameraPrefix = "";
        public static int deviceStartIndex = 0;
        public static int deviceEndIndex = int.MaxValue;
        public static string fixedTopic = "";
    }
}
