using System;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using System.Web.UI;

namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    ///     Класс-наследник от IAsyncResult для хранения параметров клиента
    /// </summary>
    public class CometAsyncState : IAsyncResult
    {
        /// <summary>
        ///     Максимальное количество предпринимаемых попыток отправки сообщений
        /// </summary>
        public const int MaxTries = 3;

        /// <summary>
        ///     Асинхронный колбек
        /// </summary>
        public AsyncCallback AsyncCallback;

        /// <summary>
        ///     Контекст, где хранить все это и был создан класс-наследник от IAsyncResult
        /// </summary>
        public HttpContext CurrentContext;

        /// <summary>
        ///     Данные
        /// </summary>
        public object ExtraData;

        /// <summary>
        ///     Очередь сообщений для отправки сообщений клиенту
        /// </summary>
        public Queue<CometMessage> Messages;

        public Page Page;

        /// <summary>
        ///     Количество оставшихся попыток отправки сообщений
        /// </summary>
        public int Tries;

        /// <summary>
        ///     Конструктор
        /// </summary>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="callback">Асинхронный обработчик</param>
        /// <param name="data">Обрабатывамые данные</param>
        public CometAsyncState(HttpContext context, AsyncCallback callback, object data)
        {
            CurrentContext = context;
            AsyncCallback = callback;
            ExtraData = data;
            IsCompleted = false;
            IsEditable = true;
            IsModified = false;
            Start = DateTime.Now;

            Tries = MaxTries;
        }

        /// <summary>
        ///     Идентификатор
        /// </summary>
        public string ClientGuid { get; set; }

        // Завершим запрос
        public void CompleteRequest()
        {
            // При завершении запроса просто выставим флаг что он завершен
            // и вызовем callback
            CometServer.WriteLog("Start CompleteRequest");
            IsCompleted = true;
            if (AsyncCallback != null)
                try
                {
                    AsyncCallback(this);
                }
                catch
                {
                }

            CometServer.WriteLog("End CompleteRequest");
        }

        /// <summary>
        ///     Добавить сообщение в очередь
        /// </summary>
        /// <param name="m"></param>
        public int AddMessage(CometMessage m)
        {
            //Клиент уже разрегистрирован
            if (Tries < 1) return -1;
            CometServer.WriteLog("Start AddMessage");

            if (null == Messages)
                Messages = new Queue<CometMessage>();

            if (Messages.Count < 1) Tries = MaxTries;

            //Если в очереди уже есть сообщение для обновления списка пользователей, то имеющееся сообщение можно просто обновить

            if (m.isUserList())
            {
                var en = Messages.GetEnumerator();
                while (en.MoveNext())
                    if (en.Current.isUserList())
                    {
                        CometServer.WriteLog($"AddMessage: {m.Message}");
                        en.Current.Message = m.Message;
                        return Messages.Count;
                    }
            }

            CometServer.WriteLog($"AddMessage: {m.Message}");
            Messages.Enqueue(m);
            CometServer.WriteLog("End AddMessage");
            return Messages.Count;
        }

        /// <summary>
        ///     Отправка первого сообщения из очереди сообщений
        /// </summary>
        /// <param name="fTest">
        ///     Если True, то метод был вызван после окончания ожидания, в случае неудачи следует уменьшить
        ///     количество оставшихся попыток
        /// </param>
        public void SendMessage(bool fTest)
        {
            CometServer.WriteLog("Start SendMessage");
            if (Messages == null)
            {
                CometServer.WriteLog("End SendMessage: Messages == null");
                return;
            }


            if (Messages.Count < 1)
            {
                CometServer.WriteLog("End SendMessage: Messages.Count < 1");
                return;
            }

            var fFail = true;
            if (CurrentContext != null && CurrentContext.Session != null)
            {
                var message = Messages.Peek();

                try
                {
                    // пишем в выходной поток текущее сообщение
                    CometServer.WriteLog($"SendMessage: {message.Message}");
                    CurrentContext.Response.Write(message.IsV4Script
                        ? message.Message
                        : message.Serialize());

                    Messages.Dequeue();
                    fFail = false;

                    //Сбрасываем счетчик неудачных попыток после каждой успешной отправки сообщения, если клиент не должен быть удален (например после Unregister)
                    if (Tries > 0) Tries = MaxTries;
                }
                catch
                {
                }
            }

            // После чего завершаем запрос - вот именно после этого результаты
            // запроса пойдут ко всем подключенным клиентам
            CompleteRequest();
            CurrentContext = null;

            if (fFail && fTest && --Tries < 1) Messages = null;
            CometServer.WriteLog("End SendMessage");
        }

        #region IAsyncResult Members

        /// <summary>
        ///     Заглушка для интерфейса IAsyncResult
        ///     Возвращает значение, показывающее, синхронно ли закончилась асинхронная операция.
        /// </summary>
        public bool CompletedSynchronously => false;

        /// <summary>
        ///     Признак завершения запроса
        /// </summary>
        public bool IsCompleted { get; set; }


        /// <summary>
        ///     Объект редактируется или просматривается
        /// </summary>
        public bool IsEditable { get; set; }

        /// <summary>
        ///     Объект отредактирован
        /// </summary>
        public bool IsModified { get; set; }

        /// <summary>
        ///     Время последнего обновления Long-Polling соединения
        /// </summary>
        public DateTime Start { get; set; }

        /// <summary>
        ///     Идентификатор сущности
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        ///     Название сущности
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     Возвращает определенный пользователем объект, который определяет или содержит в себе сведения об асинхронной
        ///     операции.
        /// </summary>
        public object AsyncState => ExtraData;

        /// <summary>
        ///     Возвращает дескриптор WaitHandle, используемый для ожидания завершения асинхронной операции.
        /// </summary>
        public WaitHandle AsyncWaitHandle => new ManualResetEvent(false);

        #endregion
    }
}