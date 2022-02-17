using System.Collections.Generic;
using UnityEngine;
using IO.Ably;
using IO.Ably.Realtime;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections;

public class AblyTest : MonoBehaviour
{
    [Header("Params")]
    [SerializeField]
    private bool _useChannelRewind = false;
    [SerializeField]
    private float _typingInactiveDuration = 2f;

    [Header("References")]
    [SerializeField]
    private TMP_InputField _usernameField;
    [SerializeField]
    private TMP_InputField _channelField;
    [SerializeField]
    private Button _connectButton;
    [SerializeField]
    private Button _disconnectButton;
    [SerializeField]
    private TextMeshProUGUI _connectionStatusText;
    [SerializeField]
    private TMP_Text _channelStatusText;
    [SerializeField]
    private TMP_Text _channelEventText;
    [SerializeField]
    private TMP_InputField _messageField;
    [SerializeField]
    private Button _sendButton;
    [SerializeField]
    private ScrollRect _presenceScrollRect;
    [SerializeField]
    private GameObject _presencePrefab;
    [SerializeField]
    private ScrollRect _chatScrollRect;
    [SerializeField]
    private GameObject _chatPrefab;
    [SerializeField]
    private GameObject _chatSelfPrefab;
    [SerializeField]
    private TMP_Text _isTypingText;

    private AblyRealtime Ably;
    private IRealtimeChannel Channel;

    private Dictionary<string, Chat> ChatDict = new Dictionary<string, Chat>();
    private Dictionary<string, Presence> PresenceDict = new Dictionary<string, Presence>();

    private UnityMainThread UnityMainThread;

    private float TypingInactiveTimer = 0;

    private bool Disconnecting = false;

    private class UnityAblyLogSink : ILoggerSink
    {
        public void LogEvent(LogLevel level, string message)
        {
            switch (level)
            {
                default:
                    Debug.Log(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
            }
        }
    }

    private RectTransform ChatContentParent;
    private bool IsChatFilledToTop => _chatScrollRect.content.rect.height >= ChatContentParent.rect.height;
    private bool IsCheckingHistory = false;
    private bool NoMoreHistory = false;

    private const string TimeStampFormat = "dd/MM/yyyy HH:mm:ss:fff zzz";

    private Chat OldestChat
    {
        get
        {
            Chat oldest = null;
            foreach (var chat in ChatDict.Values)
            {
                if (oldest == null || chat.TimeStamp < oldest.TimeStamp)
                {
                    oldest = chat;
                }
            }
            return oldest;
        }
    }

    private Chat NewestChat
    {
        get
        {
            Chat newest = null;
            foreach (var chat in ChatDict.Values)
            {
                if (newest == null || chat.TimeStamp > newest.TimeStamp)
                {
                    newest = chat;
                }
            }
            return newest;
        }
    }

    private (Chat oldest, Chat newest) OldestAndNewestChat
    {
        get
        {
            Chat oldest = null;
            Chat newest = null;
            foreach (var chat in ChatDict.Values)
            {
                if (oldest == null || chat.TimeStamp < oldest.TimeStamp)
                {
                    oldest = chat;
                }
                if (newest == null || chat.TimeStamp > newest.TimeStamp)
                {
                    newest = chat;
                }
            }
            return (oldest, newest);
        }
    }

    private void Awake()
    {
        Debug.Log($"AblyTest Awake, threadId: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

        UnityMainThread = UnityMainThread.CreateInstance();

        _connectButton.onClick.AddListener(Connect);
        _disconnectButton.onClick.AddListener(Disconnect);
        _sendButton.onClick.AddListener(Publish);
        _messageField.onSubmit.AddListener(Publish);
        _messageField.onSubmit.AddListener((message) =>
        {
            _messageField.ActivateInputField();
        });
        _messageField.onValueChanged.AddListener((message) => SetIsTyping());

        ChatContentParent = _chatScrollRect.content.parent as RectTransform;
    }

    private void Update()
    {
        if (TypingInactiveTimer > 0)
        {
            TypingInactiveTimer -= Time.unscaledDeltaTime;
            if (TypingInactiveTimer <= 0)
            {
                SetIsNotTyping();
            }
        }

        // check previous chat history if user scroll to top of chat window (only if not using channel rewind)
        if (!_useChannelRewind && Ably != null && Channel != null && Channel.State == ChannelState.Attached && !NoMoreHistory && !IsCheckingHistory && (_chatScrollRect.verticalNormalizedPosition <= 0 || !IsChatFilledToTop))
        {
            Debug.Log($"check for more history");
            // check for more history
            CheckPreviousHistoryAsync();
        }
    }

    private async void Connect()
    {
        if (Disconnecting)
        {
            Debug.LogWarning("Can't connect when disconnecting");
            return;
        }

        if (Ably != null)
        {
            return;
        }

        var username = _usernameField.text;
        var channel = _channelField.text;

        var options = new ClientOptions("8ct58Q.rcb4CQ:AUjLRpat8dq532OVL4HYxR4WmHiP3bhL6CSkEuThQGg")
        {
            ClientId = username,
            // this will disable the library trying to subscribe to network state notifications and causing "EntryPointNotFoundException: CreateNLSocket error"
            AutomaticNetworkStateMonitoring = false,
            LogHandler = new UnityAblyLogSink(),
            AutoConnect = false
        };
        Ably = new AblyRealtime(options);
        Ably.Connection.ConnectionStateChanged += ConnectionStateChanged;
        SetConnectionStateText(Ably.Connection.State);
        Ably.Connect();

        var channelParams = new ChannelParams();
        // BUG: Enabling channel rewind have 2 bugs:
        // 1. Cannot get history beyond 2 minutes even though persistent history is enabled on the channel
        // 2. After entering presence (after previously connected and chatted), the client receives a Leave presence message after the Enter presence message
        if (_useChannelRewind)
        {
            channelParams.Add("rewind", "100");
        }
        var channelOptions = new ChannelOptions();
        channelOptions.Params = channelParams;
        Channel = Ably.Channels.Get(channel, channelOptions);
        Channel.StateChanged += ChannelStateChanged;
        SetChannelStateText(Channel.State);
        // this implicitly attaches the channel
        Channel.Subscribe(ChannelHandler);

        Debug.Log("Entering Presence");
        // this will also implicitly attaches to the channel (if its not before)
        var enterPresenceResult = await Channel.Presence.EnterAsync(Presence.Status.Online.ToString());
        if (!(enterPresenceResult?.IsSuccess ?? false))
        {
            Debug.LogError($"Entering Presence Failed: {enterPresenceResult?.Error?.ToString()}");
        }
        else
        {
            Debug.Log("Presence entered");
        }
        Channel.Presence.Subscribe(PresenceHandler);

        GetPresenceAsync();
        if (!_useChannelRewind)
        {
            // Get 10 previous chat history
            CheckPreviousHistoryAsync(10);
        }
    }

    private void ConnectionStateChanged(object sender, ConnectionStateChange state)
    {
        UnityMainThread.RunOnMainThread(() =>
        {
            SetConnectionStateText(state.Current);
        });
    }

    private void SetConnectionStateText(ConnectionState state)
    {
        _connectionStatusText.text = state.ToString();
    }

    private void ChannelStateChanged(object sender, ChannelStateChange state)
    {
        // TODO: if channel event is Failed or Suspended, we should just restart the chat (delete all chat, reattach channel, re-enter presence, CheckPreviousHistoryAsync again)
        // BUG: even if channel is never suspended, message continuity is always lost (always state.Resumed = false)
        // TODO: because of above bug, we need to manually resync chat after Attached or Update event
        UnityMainThread.RunOnMainThread(() =>
        {
            SetChannelStateText(state.Current);
            SetChannelEventText(state.Event, state.Resumed);
            Debug.Log($"ChannelStateChanged, state.Current: {state.Current}, state.Previous: {state.Previous}, state.Event: {state.Event}, state.Resumed: {state.Resumed}");
        });
    }

    private void SetChannelStateText(ChannelState state)
    {
        _channelStatusText.text = state.ToString();
    }

    private void SetChannelEventText(ChannelEvent state, bool resumed)
    {
        var text = state.ToString();
        if (resumed)
        {
            text += ", Resumed";
        }
        _channelEventText.text = text;
    }

    private void ChannelHandler(Message message)
    {
        UnityMainThread.RunOnMainThread(() =>
        {
            SpawnChat(message);
            Debug.Log($"Received a message, Data: {message.Data}, Name: {message.Name}, ClientId: {message.ClientId}, Timestamp: {message.Timestamp.GetValueOrDefault().ToString(TimeStampFormat)}");
        });
    }

    private void PresenceHandler(PresenceMessage message)
    {
        //BUG: after disconnecting and reconnecting, client will still automatically Leave the presence and will not re-enter
        //TODO: because of above bug, we need to Update or Enter presence again on ChannelEvent Attached or Update
        UnityMainThread.RunOnMainThread(() =>
        {
            TryAddOrUpdatePresence(message);
            Debug.Log($"Received a presence message, Data: {message.Data}, ClientId: {message.ClientId}, Action: {message.Action}, Timestamp: {message.Timestamp.GetValueOrDefault().ToString(TimeStampFormat)}");
        });
    }

    private void Disconnect()
    {
        if (Disconnecting)
        {
            Debug.LogWarning("Can't disconnect when disconnecting");
            return;
        }
        Disconnecting = true;

        if (Ably != null)
        {
            var ably = Ably;
            Ably.Close();
            Ably = null;
            Channel = null;

            if (UnityMainThread != null)
            {
                UnityMainThread.StartCoroutine(Routine());
                IEnumerator Routine()
                {
                    yield return new WaitForSeconds(1);
                    ably.Dispose();
                }
            }
            else
            {
                ably.Dispose();
            }
        }
        DeleteAllChat();
        DeleteAllPresence();
        NoMoreHistory = false;
        IsCheckingHistory = false;
        Disconnecting = false;
    }

    private void OnDestroy()
    {
        Disconnect();
        if (UnityMainThread != null)
        {
            UnityMainThread.StartCoroutine(Routine());
            IEnumerator Routine()
            {
                yield return new WaitForSeconds(1);
                UnityMainThread.DestroyInstance();
            }
        }
    }

    private void SetIsTyping()
    {
        if (TypingInactiveTimer <= 0)
            Channel?.Presence.Update(Presence.Status.Typing.ToString());
        TypingInactiveTimer = _typingInactiveDuration;
    }

    private void SetIsNotTyping()
    {
        TypingInactiveTimer = 0;
        Channel?.Presence.Update(Presence.Status.Online.ToString());
    }

    private void Publish()
    {
        Publish(_messageField.text);
    }
    private void Publish(string message)
    {
        if (Ably != null && Channel != null && !string.IsNullOrWhiteSpace(message))
        {
            Channel.Publish(_usernameField.text, message);
            _messageField.text = "";
        }
    }

    private Chat SpawnChat(Message message)
    {
        return SpawnChat(message.Id, message.ClientId, message.Data.ToString(), message.Timestamp.GetValueOrDefault().LocalDateTime);
    }

    private Chat SpawnChat(string id, string username, string message, DateTime dateTime)
    {
        if (!ChatDict.ContainsKey(id))
        {
            var chatPrefab = username == _usernameField.text ? _chatSelfPrefab : _chatPrefab;
            var go = Instantiate(chatPrefab, _chatScrollRect.content);
            var chat = go.GetComponent<Chat>();
            var isOnline = false;
            if (PresenceDict.TryGetValue(username, out var presence))
            {
                isOnline = presence.CurrStatus != Presence.Status.Offline;
            }
            chat.SetChat(id, username, message, dateTime, isOnline);
            ChatDict.Add(id, chat);
            return chat;
        }
        return null;
    }

    private void DeleteAllChat()
    {
        foreach (var chat in ChatDict.Values)
        {
            Destroy(chat.gameObject);
        }
        ChatDict.Clear();
    }

    private Presence SpawnPresence(PresenceMessage message)
    {
        if (!Enum.TryParse<Presence.Status>(message.Data.ToString(), true, out var presenceStatus))
        {
            presenceStatus = Presence.Status.Offline;
        }
        return SpawnPresence(message.ClientId, presenceStatus);
    }

    private Presence SpawnPresence(string clientId, Presence.Status status)
    {
        if (!PresenceDict.ContainsKey(clientId))
        {
            var go = Instantiate(_presencePrefab, _presenceScrollRect.content);
            var presence = go.GetComponent<Presence>();
            presence.SetPresence(clientId, status);
            PresenceDict.Add(clientId, presence);
            return presence;
        }
        return null;
    }

    private void DeleteAllPresence()
    {
        foreach (var presence in PresenceDict.Values)
        {
            Destroy(presence.gameObject);
        }
        PresenceDict.Clear();
    }

    private void DeletePresence(string ClientId)
    {
        if (PresenceDict.TryGetValue(ClientId, out var presence))
        {
            DeletePresence(presence);
        }
    }

    private void DeletePresence(Presence presence)
    {
        Destroy(presence.gameObject);
        PresenceDict.Remove(presence.ClientId);
    }

    private async void CheckPreviousHistoryAsync(int limit = 5)
    {
        IsCheckingHistory = true;
        var oldestTime = DateTimeOffset.Now;
        var oldestChat = OldestChat;
        var oldestId = string.Empty;
        if (oldestChat != null)
        {
            // subtract 1 millisecond to prevent querying the oldest message again
            oldestTime = oldestChat.TimeStamp.Subtract(TimeSpan.FromMilliseconds(1));
            oldestId = oldestChat.Id;
        }
        Debug.Log($"oldestTime: {oldestTime.ToString(TimeStampFormat)}");

        var query = new PaginatedRequestParams
        {
            Limit = limit,
            End = oldestTime
        };
        var result = await Channel.HistoryAsync(query);
        Debug.Log($"result.Items.Count: {result.Items.Count}");
        if (result.Items.Count > 0)
        {
            TryAddHistoryMessages(result.Items);
        }
        if (result.Items.Count == 0 || result.Items.Count < limit)
        {
            NoMoreHistory = true;
        }

        IsCheckingHistory = false;
        Debug.Log($"checking history finished, NoMoreHistory: {NoMoreHistory}");
    }

    private void TryAddHistoryMessages(IEnumerable<Message> messages)
    {
        foreach (var message in messages)
        {
            var chat = SpawnChat(message);
            if (chat != null)
            {
                chat.transform.SetAsFirstSibling();
            }
        }
    }

    private async void GetPresenceAsync()
    {
        Debug.Log($"GetPresenceAsync");
        var presences = await Channel.Presence.GetAsync();
        foreach (var presence in presences)
        {
            Debug.Log($"GetPresenceAsync, Data: {presence.Data}, ClientId: {presence.ClientId}, Action: {presence.Action}, Timestamp: {presence.Timestamp.GetValueOrDefault().ToString(TimeStampFormat)}");
        }
        TryAddOrUpdatePresence(presences);
    }

    private void TryAddOrUpdatePresence(IEnumerable<PresenceMessage> messages)
    {
        foreach (var message in messages)
        {
            TryAddOrUpdatePresence(message, false);
        }
        UpdateIsTypingText();
    }

    private void TryAddOrUpdatePresence(PresenceMessage message, bool updateIsTyping = true)
    {
        if (!Enum.TryParse<Presence.Status>(message.Data.ToString(), true, out var presenceStatus))
        {
            presenceStatus = Presence.Status.Offline;
        }
        if (message.Action == PresenceAction.Absent || message.Action == PresenceAction.Leave)
        {
            presenceStatus = Presence.Status.Offline;
        }

        var isOffline = presenceStatus == Presence.Status.Offline;

        if (!PresenceDict.TryGetValue(message.ClientId, out var presence) && !isOffline)
        {
            presence = SpawnPresence(message);
        }
        if (presence != null)
        {
            if (!isOffline)
            {
                presence.SetStatus(presenceStatus);
            }
            else
            {
                DeletePresence(presence);
            }
        }

        foreach (var chat in ChatDict.Values)
        {
            if (chat.ClientId == message.ClientId)
            {
                chat.SetOnlineStatus(presenceStatus != Presence.Status.Offline);
            }
        }

        if (!isOffline && updateIsTyping) UpdateIsTypingText();
    }

    private void UpdateIsTypingText()
    {
        var text = "";
        int i = 0;

        foreach (var presence in PresenceDict.Values)
        {
            if (presence.CurrStatus == Presence.Status.Typing && presence.ClientId != _usernameField.text)
            {
                if (i == 0)
                {
                    text = $"{presence.ClientId}";
                }
                else
                {
                    text += $", {presence.ClientId}";
                }
                i++;
            }
        }

        if (i > 0)
        {
            if (i == 0)
            {
                text += " is typing...";
            }
            else
            {
                text += " are typing...";
            }
            _isTypingText.text = text;

            if (_isTypingText.isTextOverflowing)
            {
                _isTypingText.text = "A lot of people are typing...";
            }
        }
        else
        {
            _isTypingText.text = "";
        }
    }
}
