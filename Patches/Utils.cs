using EFT.Communications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuickSell.Patches
{
    public class Utils
    {
        public static void SendNotification(string text)
        {
            NotificationManagerClass.DisplayMessageNotification(text, ENotificationDurationType.Long);
        }

        public static void SendDebugNotification(string text)
        {
            if (Plugin.Debug) NotificationManagerClass.DisplayMessageNotification(text, ENotificationDurationType.Long);
        }

        public static void SendError(string text)
        {
            NotificationManagerClass.DisplayWarningNotification(text, ENotificationDurationType.Long);
        }
    }
}
