using TMPro;
using UnityEngine;

public class Presence : MonoBehaviour
{
    [SerializeField]
    private TMP_Text _usernameText;
    [SerializeField]
    private TMP_Text _statusText;

    public string ClientId;
    public Status CurrStatus;

    public enum Status
    {
        Offline,
        Online,
        Typing
    }

    public void SetPresence(string clientId, Status status)
    {
        ClientId = clientId;
        _usernameText.SetText(clientId);
        SetStatus(status);
    }

    public void SetStatus(Status status)
    {
        CurrStatus = status;
        _statusText.SetText(status.ToString());
    }
}
