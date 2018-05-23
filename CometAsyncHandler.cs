using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.SessionState;

namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    /// Асинхронный хендлер для обработки клиентских запросов, поддерживает постоянное соединение с IIS
    /// </summary>
    public class CometAsyncHandler : IHttpAsyncHandler, IReadOnlySessionState
    {
        // Основная функция рабочего потока
        private void RequestWorker(Object obj)
        {
            // obj - второй параметр при вызове ThreadPool.QueueUserWorkItem()
            var state = obj as CometAsyncState;

            if (state == null) return;

            var command = "";
            var guid = "";
            var message = "";
            var isEditable = false;
            var id = 0;

            try
            {
                message = state.CurrentContext.Request.QueryString["message"];
            }
            catch
            {
                // ignored
            }
            finally
            {
                command = state.CurrentContext.Request.QueryString["cmd"];
                guid = state.CurrentContext.Request.QueryString["guid"];
                isEditable = state.CurrentContext.Request.QueryString["Editable"] == "true";
                id = int.Parse(state.CurrentContext.Request.QueryString["id"] ?? "0");
            }

            state.IsEditable = isEditable;
            state.Id = id;

            switch (command)
            {
                //case "register":
                //    // Регистрируем клиента в очереди сообщений
                //    state.ClientGuid = guid;
                //    CometServer.RegisterClient(state);
                //    state.CompleteRequest();
                //    break;
                //case "unregister":
                //    // Удаляем клиента из очереди сообщений
                //    state.ClientGuid = guid;
                //    CometServer.UnregisterClient(state);
                //    state.CompleteRequest();
                //    break;
                //case "connect":
                //    if (guid != null)
                //        CometServer.UpdateClient(state, guid, true);
                //    break;
                case "update":
                    CometServer.PushMessage(new CometMessage { Message = "", UserName = "", Status = 0, ClientGuid = guid }, guid);
                    state.CompleteRequest();
                    break;
                case "send":
                    // Отправка сообщения
                    var data = new StreamReader(state.CurrentContext.Request.InputStream).ReadToEnd();

                    if (data.Length > 0)
                    {
                        var jdata = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(data);
                        message = HttpUtility.UrlDecode(jdata["message"]);
                    }

                    state.CompleteRequest();
                    CometServer.OnNotifyMessage(id.ToString(), guid, message);
                    break;
                default:
                    // При реконнекте клиента
                    if (guid != null)
                    {
                        CometServer.UpdateClient(state, guid);
                    }
                    break;
            }

            CometServer.Process();
        }


        #region IHttpAsyncHandler Members

        public IAsyncResult BeginProcessRequest(HttpContext ctx, AsyncCallback cb, Object obj)
        {
            // Готовим объект для передачи его в QueueUserWorkItem
            var currentAsyncState = new CometAsyncState(ctx, cb, obj);

            RequestWorker(currentAsyncState);

            return currentAsyncState;
        }

        public void EndProcessRequest(IAsyncResult ar)
        {
        }

        #endregion


        #region IHttpHandler Members

        // IHttpHandler Members - просто пустые заглушки, так как нам не требуется реализация синхронных методов

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
        }

        #endregion
    }
}

