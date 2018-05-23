using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace Kesco.Lib.Web.Comet
{
    // Класс, описывающий одно сообщение от клиента и метод его сериализации
    public class CometMessage
    {
        public string ClientGuid;
        public string Message;
        public int Status;
        public string UserName;
        public bool IsV4Script = false;

        public string Serialize()
        {
            return "{'user': '" + HttpUtility.JavaScriptStringEncode(UserName) +
                   "', 'message': '" + HttpUtility.JavaScriptStringEncode(Message) +
                   "', 'status': '" + Status +
                   "', 'isV4Script': '" + (IsV4Script ? 1 : 0) +
                   "', 'guid': '" + ClientGuid + "'}";
        }

        public bool isUserList()
        {
            return UserName.Length < 1 && IsV4Script && Status == 0;
        }
    }
}
