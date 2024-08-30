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
        public static void sendNotification(string text)
        {
            NotificationManagerClass.DisplayMessageNotification(text, ENotificationDurationType.Long);
        }

        public static void sendError(string text)
        {
            NotificationManagerClass.DisplayWarningNotification(text, ENotificationDurationType.Long);
        }
    }
}
