using System.Web;

namespace Kesco.Lib.Web.Comet
{
    // Класс, описывающий одно сообщение от клиента и метод его сериализации
    public class CometMessage
    {
        public string ClientGuid { get; set; }
        public string Message { get; set; }
        public int Status { get; set; }
        public int Reload { get; set; }
        public string UserName { get; set; }
        public bool IsV4Script { get; set; }

        public string Serialize()
        {
            return "{'user': '" + HttpUtility.JavaScriptStringEncode(UserName) +
                   "', 'message': '" + HttpUtility.JavaScriptStringEncode(Message) +
                   "', 'status': '" + Status +
                   "', 'reload': '" + Reload +
                   "', 'isV4Script': '" + (IsV4Script ? 1 : 0) +
                   "', 'guid': '" + ClientGuid + "'}";
        }

        public bool isUserList()
        {
            return UserName.Length < 1 && IsV4Script && Status == 0;
        }
    }
}