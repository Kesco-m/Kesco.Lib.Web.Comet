using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using System.Web.SessionState;

using System.Threading;
using System.IO;
using System.Text;


namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    ///     Делегат вызывается после обновление списка клиентов
    /// </summary>
    /// <param name="sender">Инициатор вызова</param>
    public delegate void NotifyClientsEventHandler(CometAsyncState state, string clientGuid = null, int status = 0);

    public delegate void NotifySendMessageEventHandler(string id, string clientGuid, string message);

    // Собственно, серверная часть
    public static class CometServer
    {
        //public static FileStream fs = File.Create("C:\\Temp\\comet.txt");

        /// <summary>
        ///     Событие
        /// </summary>
        public static event NotifyClientsEventHandler NotifyClients;

        static void OnNotifyClients(CometAsyncState state, string clientGuid = null, int status = 0)
        {
            if (NotifyClients != null)
            {
                if ((clientGuid == null && state == null) ||
                    (clientGuid == null && state != null && state.ClientGuid == null)) return;

                NotifyClients(state, clientGuid, status);
            }
        }

        public static event NotifySendMessageEventHandler NotifyMessages;

        public static void OnNotifyMessage(string id, string clientGuid, string message)
        {
            if (NotifyMessages != null)
                NotifyMessages(id, clientGuid, message);
        }

        /// <summary>
        /// Класс объекта обрабатывающего Long Pooling запросы
        /// </summary>
        private class MsgProcessor
        {
            private const int timeout = 60000; //1 минута
            private static bool fStop = false;

            private static AutoResetEvent msgEvent = new AutoResetEvent(false);
            private static AutoResetEvent stopEvent = new AutoResetEvent(false);

            private static WaitHandle[] waitHandles = new WaitHandle[] { msgEvent, stopEvent };

            /// <summary>
            /// Добавить сообщение в очередь для клиентов удовлетворяющих заданному условию
            /// </summary>
            /// <param name="m"></param>
            /// <param name="filter"></param>
            public static void AddMessage(CometMessage m, Predicate<CometAsyncState> filter)
            {
                lock (CometServer.SyncLock)
                {
                    foreach (CometAsyncState clientState in CometServer.Connections)
                    {
                        if (filter(clientState))
                        {
                            clientState.AddMessage(m);
                        }
                    }
                }
            }

            /// <summary>
            /// Добавить сообщение в очередь для клиентов с указанным guid
            /// </summary>
            /// <param name="m"></param>
            /// <param name="clientGuid"></param>
            public static void AddMessage(CometMessage m, string clientGuid = "")
            {
                if (clientGuid == "")
                {
                    Predicate<CometAsyncState> f = (client) => { return clientGuid == "" || client.ClientGuid == clientGuid; };
                    AddMessage(m, f);
                }
                else
                {
                    CometAsyncState client = CometServer.Connections.FirstOrDefault(c => c.ClientGuid == clientGuid);
                    if (client != null)
                        client.AddMessage(m);
                }
            }

            public static void Processing(Object obj)
            {
                while (!fStop)
                {
                    int result = WaitHandle.WaitAny(waitHandles, timeout);

                    if (result == 1)
                        break;

                    lock (CometServer.SyncLock)
                    {
                        foreach (CometAsyncState clientState in CometServer.Connections)
                            clientState.SendMessage(WaitHandle.WaitTimeout == result);

                        int before = Connections.Count;
                        Connections.RemoveAll(
                            st => st.Tries < 1 && (null == st.Messages || st.Messages.Count < 1));

                        if (before != Connections.Count)
                        {
                            StringBuilder sb = new StringBuilder();

                            foreach (CometAsyncState clientState in CometServer.Connections)
                            {
                                sb.Append(" " + clientState.ClientGuid.ToString() + " " + clientState.Tries.ToString() + " " + clientState.Start.ToLongTimeString());
                            }

                            /*
                            lock (fs)
                            {
                                byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "Connections" + CometServer.Connections.Count.ToString() + sb.ToString());
                                fs.Write(g, 0, g.Length);
                            }
                            */
                        }
                    }
                }
            }

            public static void TryNow()
            {
                msgEvent.Set();
            }

            public static void Stop()
            {
                stopEvent.Set();
            }
        }

        /// <summary>
        /// Поток обработки сообщений в очередях
        /// </summary>
        private static Thread _procesor = null;

        // вспомогательный объект для блокировки ресурсов многопоточного приложения
        private static readonly Object _lock = new Object();

        public static Object SyncLock
        {
            get { return _lock; }
        }

        // Список, хранящий состояние всех подключенных клиентов
        private static readonly List<CometAsyncState> _clientStateList = new List<CometAsyncState>();
        // Возвращаем сообщение каждому подключенному клиенту
        /*
        public static void PushMessage(CometMessage pushMessage, string clientGuid = "")
        {
            lock (_lock)
            {
                // Пробегаем по списку всех подключенных клиентов
                foreach (var clientState in _clientStateList)
                {
                    if (clientState.CurrentContext.Session != null)
                    {
                        if (clientGuid != "" && clientState.ClientGuid != clientGuid) continue;
                        // И пишем в выходной поток текущее сообщение
                        clientState.CurrentContext.Response.Write(pushMessage.IsV4Script
                            ? pushMessage.Message
                            : pushMessage.Serialize());
                        // После чего завершаем запрос - вот именно после этого результаты 
                        // запроса пойдут ко всем подключенным клиентам
                        clientState.CompleteRequest();
                    }
                }
            }
        }*/

        public static void PushMessage(CometMessage message, string clientGuid = "")
        {
            MsgProcessor.AddMessage(message, clientGuid);
        }

        public static void PushMessage(CometMessage message, Predicate<CometAsyncState> filter)
        {
            MsgProcessor.AddMessage(message, filter);
        }

        /// <summary>
        /// Метод запуска сервера
        /// </summary>
        public static void Start()
        {
            _procesor = new Thread(MsgProcessor.Processing);
            _procesor.Start();
        }

        /// <summary>
        /// Метод запуска сервера
        /// </summary>
        public static void Process()
        {
            MsgProcessor.TryNow();
        }

        /// <summary>
        /// Метод остановки сервера
        /// </summary>
        public static void Stop()
        {
            lock (_lock)
            {
                //Нет смысла прерывать запрос, клиенты его сразу же начнут восстанавливать
                //_clientStateList.ForEach(c=>c.CompleteRequest());
                _clientStateList.Clear();
            }

            MsgProcessor.Stop();
            _procesor.Join();

            //fs.Close();
        }

        // Срабатывает кажды раз при запуске клиентом очережного запроса Long poll
        // так как при этом HttpContext клиента изменяется, то надо обновить
        // все изменившиеся данные клиента в списке, идентифицируемом по гуиду,
        // который у клиента в течение работы остается постоянным
        public static void UpdateClient(CometAsyncState state, string clientGuid, bool firstConnect = false)
        {
            lock (_lock)
            {
                // ищем клиента в списке по его гуиду
                var clientState = _clientStateList.FirstOrDefault(s => s.ClientGuid == clientGuid);
                if (clientState != null)
                {
                    /*
                    lock (fs)
                    {
                        byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "Updated " + clientGuid);
                        fs.Write(g, 0, g.Length);
                    }
                    */

                    // и если он нашелся, то обновляем все его параметры
                    if (clientState.Start != DateTime.MinValue)
                        clientState.Start = DateTime.Now;

                    clientState.IsCompleted = false;
                    clientState.Id = state.Id;
                    clientState.IsEditable = state.IsEditable;
                    clientState.CurrentContext = state.CurrentContext;
                    clientState.ExtraData = state.ExtraData;
                    clientState.AsyncCallback = state.AsyncCallback;
                }
            }
            /*
            if (firstConnect && state.Id > 0)
            {
                lock (fs)
                {
                    byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "OnNotifyClients from UpdateClient");
                    fs.Write(g, 0, g.Length);
                }

                OnNotifyClients(state, clientGuid);
            }*/
        }

        public static List<CometAsyncState> Connections
        {
            get
            {
                return _clientStateList;
            }
        }

        public static void ClearExpiredConnections()
        {
            

            List<string> listClientGuid = new List<string>();
            lock (_lock)
            {
                DateTime expiredDate = DateTime.Now;
                for (var i = _clientStateList.Count - 1; i > -1; i--)
                {
                    var clientState = _clientStateList[i];
                    var workTime = expiredDate - clientState.Start;
                    if (!(workTime.TotalMinutes > 15))
                    {
                        //После следующей проверки клент, который только зарегистрировался и не вел никакой активности будет удален
                        //if (clientState.Start == DateTime.MaxValue) clientState.Start = expiredDate;
                        continue;
                    }

                    listClientGuid.Add(clientState.ClientGuid);

                    PushMessage(new CometMessage
                    {
                        Message = "unregister",
                        UserName = clientState.CurrentContext==null ? "" : clientState.CurrentContext.User.Identity.Name,
                        Status = 1,
                        ClientGuid = clientState.ClientGuid
                    });

                    _clientStateList.RemoveAt(i);

                }
            }

            foreach (var x in listClientGuid)
            {
                /*
                lock (fs)
                {
                    byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "OnNotifyClients from ClearExpiredConnections");
                    fs.Write(g, 0, g.Length);
                }
                */
                OnNotifyClients(null, x, 1);
            }

        }

        // Регистрация клиента
        public static void RegisterClient(CometAsyncState state)
        {
            lock (_lock)
            {
                // Присваиваем гуид и добавляем в список
                if (!_clientStateList.Exists(x => x.ClientGuid == state.ClientGuid))
                    _clientStateList.Add(state);

                StringBuilder sb = new StringBuilder();

                foreach (CometAsyncState clientState in CometServer.Connections)
                {
                    sb.Append(" " + clientState.ClientGuid.ToString() + " " + clientState.Tries.ToString() + " " + clientState.Start.ToLongTimeString());
                }

                /*
                lock (fs)
                {
                    byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "RegisterClient" + CometServer.Connections.Count.ToString() + sb.ToString());
                    fs.Write(g, 0, g.Length);
                }
                 * */
            }

            OnNotifyClients(state, state.ClientGuid);

        }

        // Разрегистрация клиента
        public static void UnregisterClient(CometAsyncState client)
        {
            CometMessage m = new CometMessage
            {
                Message = "unregister",
                UserName = client.CurrentContext.User.Identity.Name,
                Status = 1,
                ClientGuid = client.ClientGuid
            };

            lock (_lock)
            {
                //Очередь сообщений очищаем, клиент уже не хочет ничего принимать
                if (null != client.Messages)
                    client.Messages.Clear();

                client.AddMessage(m);

                // Клиент будет удален из списка после отправки последнего сообщения ему
                client.Tries = 0;
            }

            if (client.Id > 0)
            {
                /*
                lock (fs)
                {
                    byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "OnNotifyClients from UnregisterClient(CometAsyncState client)");
                    fs.Write(g, 0, g.Length);
                }
                */
                OnNotifyClients(client, null, 1);
            }
        }

        // Разрегистрация клиента
        public static void UnregisterClient(string clientGuid, bool serverCompeteRequest = true)
        {
            CometMessage m = new CometMessage
            {
                Message = "unregister",
                UserName = HttpContext.Current == null ? "noname" : HttpContext.Current.User.Identity.Name,
                Status = 1,
                ClientGuid = clientGuid
            };

            lock (_lock)
            {
                // Клиент будет удален из списка после отправки последнего сообщения ему
                CometAsyncState client = _clientStateList.FirstOrDefault(x => x.ClientGuid == clientGuid);

                if (client != null)
                {
                    //Очередь сообщений очищаем, клиент уже не хочет ничего принимать
                    if (null != client.Messages)
                        client.Messages.Clear();
                    
                    //если требуется закрыть соединение с сервера
                    if (serverCompeteRequest)
                        client.AddMessage(m);

                    // Клиент будет удален из списка после отправки последнего сообщения ему
                    client.Tries = 0;
                }
            }

            /*
            lock (fs)
            {
                byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "OnNotifyClients from UnregisterClient(string clientGuid) " + clientGuid);
                fs.Write(g, 0, g.Length);
            }
             * */

            OnNotifyClients(null, clientGuid, 1);
            Process();
        }
    }
}