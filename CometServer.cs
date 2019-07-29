using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;

namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    ///     Указатель на функцию, вызывается после обновления списка клиентов
    /// </summary>
    /// <param name="sender">Инициатор вызова</param>
    public delegate void NotifyClientsEventHandler(CometAsyncState state, string clientGuid = null, int status = 0);

    /// Указатель на функцию, вызывается после обновление списка клиентов
    /// </summary>
    /// <param name="sender">Инициатор вызова</param>
    public delegate void NotifySendMessageEventHandler(string id, string name, string clientGuid, string message);

    // Собственно, серверная часть Comet
    public static class CometServer
    {
        //Путь к файлу логирования comet-сервера
        public const string COMET_LOG_FILE = "C:\\scripts\\comet.txt";

        /// <summary>
        ///     Объект синхронизации записи в лог
        /// </summary>
        private static readonly object _lockCometLog = new object();

        /// <summary>
        ///     Поток обработки сообщений в очередях
        /// </summary>
        private static Thread _procesor;

        // вспомогательный объект для блокировки ресурсов многопоточного приложения

        // Список, хранящий состояние всех подключенных клиентов

        /// <summary>
        ///     Объект синхронизации потоков
        /// </summary>
        public static object SyncLock { get; } = new object();

        /// <summary>
        ///     Подключенные клиенты
        /// </summary>
        public static List<CometAsyncState> Connections { get; } = new List<CometAsyncState>();

        /// <summary>
        ///     Функция записи в лог
        /// </summary>
        /// <param name="message"></param>
        public static void WriteLog(string message)
        {
            //включить логирование
            //lock (_lockCometLog)
            //{
            //    using (var sw = File.AppendText(COMET_LOG_FILE))
            //    {
            //        sw.WriteLine(DateTime.Now + " " + message);
            //    }
            //}
        }

        /// <summary>
        ///     Указатель на событие, отслеживающие необходимость уведомления клиентов
        /// </summary>
        public static event NotifyClientsEventHandler NotifyClients;

        /// <summary>
        ///     Событие, отслеживающие необходимость уведомления клиентов
        /// </summary>
        /// <param name="state">Объект-подключения</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        /// <param name="status">Статус собфтия</param>
        private static void OnNotifyClients(CometAsyncState state, string clientGuid = null, int status = 0)
        {
            if (NotifyClients != null)
            {
                if (clientGuid == null && state == null ||
                    clientGuid == null && state != null && state.ClientGuid == null) return;

                NotifyClients(state, clientGuid, status);
            }
        }

        /// <summary>
        ///     Указатель на функцию обработки события получения сообщения
        /// </summary>
        public static event NotifySendMessageEventHandler NotifyMessages;

        /// <summary>
        ///     Событие, отслеживающее необходимость обработать сообщение
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="clientGuid"></param>
        /// <param name="message"></param>
        public static void OnNotifyMessage(string id, string name, string clientGuid, string message)
        {
            if (NotifyMessages != null)
                NotifyMessages(id, name, clientGuid, message);
        }
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

        /// <summary>
        ///     Отправка сообщения
        /// </summary>
        /// <param name="message">Сообщение</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        public static void PushMessage(CometMessage message, string clientGuid = "")
        {
            MsgProcessor.AddMessage(message, clientGuid);
        }

        /// <summary>
        ///     Отправка сообщения
        /// </summary>
        /// <param name="message">Сообщение</param>
        /// <param name="filter">Фильтр клиентов</param>
        public static void PushMessage(CometMessage message, Predicate<CometAsyncState> filter)
        {
            WriteLog($"Start PushMessage -> message: {message.Message}");
            MsgProcessor.AddMessage(message, filter);
            WriteLog("End PushMessage");
        }

        /// <summary>
        ///     Метод запуска сервера
        /// </summary>
        public static void Start()
        {
            WriteLog("Start CometServer");
            _procesor = new Thread(MsgProcessor.Processing);
            _procesor.Start();
            WriteLog("End Start CometServer");
        }

        /// <summary>
        ///     Метод запуска сервера
        /// </summary>
        public static void Process()
        {
            MsgProcessor.TryNow();
        }

        /// <summary>
        ///     Метод остановки сервера
        /// </summary>
        public static void Stop()
        {
            lock (SyncLock)
            {
                WriteLog("Stop CometServer:->" + Connections.Count);
                Connections.ForEach(delegate(CometAsyncState cas)
                {
                    PushMessage(
                        new CometMessage
                        {
                            Message =
                                "Внимание! Соединение с приложением потеряно. Через 5 сек. соединение будет восстановлено и Вы сможете продолжить работу.",
                            UserName = "", Status = 2, ClientGuid = cas.ClientGuid, Reload = 1
                        }, cas.ClientGuid);
                    cas.CompleteRequest();
                    WriteLog("STOP APPLICATION -> " + cas.ClientGuid);
                });
                Connections.Clear();
            }

            MsgProcessor.Stop();
            _procesor.Join();
            WriteLog("End Stop CometServer");
        }

        /// <summary>
        ///     Срабатывает кажды раз при запуске клиентом очережного запроса Long poll
        ///     так как при этом HttpContext клиента изменяется, то надо обновить
        ///     все изменившиеся данные клиента в списке, идентифицируемом по гуиду,
        ///     который у клиента в течение работы остается постоянным
        /// </summary>
        /// <param name="state">Объект-подключение</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        /// <param name="firstConnect">В первый ли раз подключается клиент</param>
        public static void UpdateClient(CometAsyncState state, string clientGuid, bool firstConnect = false)
        {
            WriteLog(
                $"Start UpdateClient CometAsyncState: {state.ClientGuid} clientGuid: {clientGuid} firstConnect: {firstConnect}");
            var existState = false;
            lock (SyncLock)
            {
                // ищем клиента в списке по его гуиду
                var clientState = Connections.FirstOrDefault(s => s.ClientGuid == clientGuid);
                if (clientState != null)
                {
                    // и если он нашелся, то обновляем все его параметры
                    if (clientState.Start != DateTime.MinValue)
                        clientState.Start = DateTime.Now;

                    clientState.IsCompleted = false;
                    clientState.Id = state.Id;
                    clientState.Name = state.Name;
                    clientState.IsEditable = state.IsEditable;
                    //clientState.IsModified = state.IsModified;
                    clientState.CurrentContext = state.CurrentContext;
                    clientState.ExtraData = state.ExtraData;
                    clientState.AsyncCallback = state.AsyncCallback;

                    existState = true;
                }
            }

            if (!existState)
            {
                if (state != null)
                {
                    state.ClientGuid = clientGuid;
                    RegisterClient(state, false);
                    WriteLog($"-> LOST CONNECTION  UnregisterClient by state -> ClientGuid: {clientGuid}");
                    UnregisterClient(state, true);
                }
                else
                {
                    throw new Exception("Отсутствует объект state! Ошибка организации вызова клиента!");
                }
            }

            WriteLog("End UpdateClient");
        }

        /// <summary>
        ///     Очистка просроченных соединенй
        /// </summary>
        public static void ClearExpiredConnections()
        {
            WriteLog("Start ClearExpiredConnections, check count=" + Connections.Count);

            var listClientGuid = new List<string>();
            lock (SyncLock)
            {
                var expiredDate = DateTime.Now;
                for (var i = Connections.Count - 1; i > -1; i--)
                {
                    var clientState = Connections[i];
                    var workTime = expiredDate - clientState.Start;


                    WriteLog($"Check ClientGuid: {clientState.ClientGuid} WorkTime:{workTime}");

                    if (!(workTime.TotalMinutes > 15)) continue;


                    WriteLog($"Unregister -> ClientGuid: {clientState.ClientGuid}");

                    listClientGuid.Add(clientState.ClientGuid);

                    PushMessage(new CometMessage
                    {
                        Message = "unregister",
                        UserName = clientState.CurrentContext == null
                            ? ""
                            : clientState.CurrentContext.User.Identity.Name,
                        Status = 1,
                        ClientGuid = clientState.ClientGuid
                    });

                    Connections.RemoveAt(i);
                }
            }

            foreach (var x in listClientGuid) OnNotifyClients(null, x, 1);

            WriteLog("End ClearExpiredConnections");
        }

        // Регистрация клиента
        public static void RegisterClient(CometAsyncState state, bool notifyClients = true)
        {
            WriteLog("+++++++++++++++++++++++++++++++++++++++++++++++++++++");
            WriteLog("Start RegisterClient by CometAsyncState -> " + state.ClientGuid + " notifyClients ->" +
                     notifyClients);
            lock (SyncLock)
            {
                // Присваиваем гуид и добавляем в список
                if (!Connections.Exists(x => x.ClientGuid == state.ClientGuid))
                    Connections.Add(state);

                WriteLog("EXISTS CONNECTIONS:");

                foreach (var clientState in Connections)
                    WriteLog(
                        $"     -> ClientGuid: {clientState.ClientGuid} Tries: {clientState.Tries} StartSession: {clientState.Start.ToLongTimeString()}");
            }

            if (notifyClients)
                OnNotifyClients(state, state.ClientGuid);

            WriteLog(
                $"End RegisterClient by CometAsyncState -> ClientGuid:{state.ClientGuid} NotifyClients: {notifyClients}");
        }

        // Разрегистрация клиента
        public static void UnregisterClient(CometAsyncState client, bool reload = false)
        {
            WriteLog($"Start UnregisterClient by CometAsyncState -> ClientGuid: {client.ClientGuid} Reload: {reload}");
            var m = new CometMessage
            {
                Message = "unregister",
                UserName = client.CurrentContext.User.Identity.Name,
                Status = 1,
                Reload = reload ? 1 : 0,
                ClientGuid = client.ClientGuid
            };

            lock (SyncLock)
            {
                //Очередь сообщений очищаем, клиент уже не хочет ничего принимать
                if (null != client.Messages)
                    client.Messages.Clear();

                client.AddMessage(m);

                // Клиент будет удален из списка после отправки последнего сообщения ему
                client.Tries = 0;
            }

            if (client.Id > 0)
                OnNotifyClients(client, null, 1);

            Process();
        }

        // Разрегистрация клиента
        public static void UnregisterClient(string clientGuid, bool serverCompeteRequest = true)
        {
            WriteLog("Start UnregisterClient by Guid -> " + clientGuid);

            var m = new CometMessage
            {
                Message = "unregister",
                UserName = HttpContext.Current == null ? "noname" : HttpContext.Current.User.Identity.Name,
                Status = 1,
                ClientGuid = clientGuid
            };

            lock (SyncLock)
            {
                // Клиент будет удален из списка после отправки последнего сообщения ему
                var client = Connections.FirstOrDefault(x => x.ClientGuid == clientGuid);

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

            OnNotifyClients(null, clientGuid, 1);
            Process();
            WriteLog("End UnregisterClient by Guid -> " + clientGuid);
        }

        /// <summary>
        ///     Класс объекта обрабатывающего Long Pooling запросы
        /// </summary>
        private class MsgProcessor
        {
            private const int timeout = 60000; //1 минута
            private static readonly bool fStop = false;

            private static readonly AutoResetEvent msgEvent = new AutoResetEvent(false);
            private static readonly AutoResetEvent stopEvent = new AutoResetEvent(false);

            private static readonly WaitHandle[] waitHandles = {msgEvent, stopEvent};

            /// <summary>
            ///     Добавить сообщение в очередь для клиентов удовлетворяющих заданному условию
            /// </summary>
            /// <param name="m">Сообщение</param>
            /// <param name="filter">Фильтр для определения клиентов</param>
            public static void AddMessage(CometMessage m, Predicate<CometAsyncState> filter)
            {
                WriteLog("Start AddMessage to clients by filter");
                lock (SyncLock)
                {
                    foreach (var clientState in Connections)
                        if (filter(clientState))
                            clientState.AddMessage(m);
                }

                WriteLog("End AddMessage to clients by filter");
            }

            /// <summary>
            ///     Добавить сообщение в очередь для клиентов с указанным guid
            /// </summary>
            /// <param name="m">Сообщение</param>
            /// <param name="clientGuid">IP- страницы</param>
            public static void AddMessage(CometMessage m, string clientGuid = "")
            {
                WriteLog("Start AddMessage to clients by guid [" + clientGuid + "]");
                if (clientGuid == "")
                {
                    Predicate<CometAsyncState> f = client =>
                    {
                        return clientGuid == "" || client.ClientGuid == clientGuid;
                    };
                    AddMessage(m, f);
                }
                else
                {
                    var client = Connections.FirstOrDefault(c => c.ClientGuid == clientGuid);
                    if (client != null)
                        client.AddMessage(m);
                }

                WriteLog("End AddMessage to clients by guid [" + clientGuid + "]");
            }

            /// <summary>
            ///     Обработка подготовленных к отправке сообщений
            /// </summary>
            /// <param name="obj">Объект, заглукшка</param>
            public static void Processing(object obj)
            {
                WriteLog("Start Processing messages");
                while (!fStop)
                {
                    var result = WaitHandle.WaitAny(waitHandles, timeout);

                    if (result == 1)
                        break;

                    lock (SyncLock)
                    {
                        foreach (var clientState in Connections)
                        {
                            WriteLog($"Check client message -> ClientGuid: {clientState.ClientGuid}");
                            clientState.SendMessage(WaitHandle.WaitTimeout == result);
                        }

                        var before = Connections.Count;
                        Connections.RemoveAll(
                            st => st.Tries < 1 && (null == st.Messages || st.Messages.Count < 1));

                        if (before != Connections.Count)
                        {
                            var sb = new StringBuilder();

                            if (Connections.Count > 0)
                            {
                                sb.Append(" EXISTS CONNECTIONS AFTER PROCESSING MESSAGES:");
                                sb.Append(Environment.NewLine);
                            }

                            foreach (var clientState in Connections)
                            {
                                sb.Append(
                                    $" -> ClientGuid: {clientState.ClientGuid} Tries: {clientState.Tries} clientState.Start: {clientState.Start.ToLongTimeString()}");
                                sb.Append(Environment.NewLine);
                            }

                            if (sb.Length > 0)
                                WriteLog(sb.ToString());
                        }
                    }
                }

                WriteLog("End Processing messages");
            }

            /// <summary>
            ///     Инициализация выполения события
            /// </summary>
            public static void TryNow()
            {
                WriteLog("Start Event TryNow");
                msgEvent.Set();
                WriteLog("End Event TryNow");
            }

            /// <summary>
            ///     Остановка события
            /// </summary>
            public static void Stop()
            {
                WriteLog("Start stop event");
                stopEvent.Set();
                WriteLog("End stop event");
            }
        }
    }
}