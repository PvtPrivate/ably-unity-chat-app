using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Chat : MonoBehaviour
{
    [SerializeField]
    private TMP_Text _usernameText;
    [SerializeField]
    private TMP_Text _message;
    [SerializeField]
    private TMP_Text _timestamp;
    [SerializeField]
    private Image _onlineStatusIcon;
    [SerializeField]
    private Color _onlineColor = Color.green;
    [SerializeField]
    private Color _offlineColor = new Color(.2f, .2f, .2f, 1f);

    public string Id;
    public string ClientId;
    public bool IsOnline;
    public DateTimeOffset TimeStamp;

    public void SetChat(string id, string clientId, string message, DateTimeOffset timeStamp, bool isOnline)
    {
        TimeStamp = timeStamp;
        Id = id;
        ClientId = clientId;
        
        _usernameText.SetText(clientId);
        _message.SetText(message);
        var dateTime = timeStamp.LocalDateTime;
        var time = $"{dateTime.ToShortDateString()}\n{dateTime.ToShortTimeString()}";
        _timestamp.SetText(time);
        SetOnlineStatus(isOnline);
    }

    public void SetOnlineStatus(bool isOnline)
    {
        IsOnline = isOnline;
        if (isOnline)
        {
            _onlineStatusIcon.color = _onlineColor;
        }
        else
        {
            _onlineStatusIcon.color = _offlineColor;
        }
    }
}