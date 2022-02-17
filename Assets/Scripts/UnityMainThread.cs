using System;
using System.Collections.Concurrent;
using UnityEngine;

public class UnityMainThread : MonoBehaviour
{
    public const string DefaultGameObjectName = "_UnityMainThread_";

    public static UnityMainThread CreateInstance()
    {
        return CreateInstance(DefaultGameObjectName);
    }
    public static UnityMainThread CreateInstance(string objectName)
    {
        var gameObject = GameObject.Find(objectName);
        if (gameObject == null)
        {
            gameObject = new GameObject(objectName);
            DontDestroyOnLoad(gameObject);
        }
        var instance = gameObject.AddComponent<UnityMainThread>();
        return instance;
    }

    protected int _mainThreadId;

    protected volatile bool _queued = false;
    protected ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
    protected ConcurrentQueue<Action> _workload = new ConcurrentQueue<Action>();

    public void RunOnMainThread(Action action)
    {
        //check if unity object is destroyed
        if (this != null)
        {
            if (_mainThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId)
            {
                action?.Invoke();
            }
            else
            {
                lock (_queue)
                {
                    _queue.Enqueue(action);
                    _queued = true;
                }
            }
        }
    }

    protected void Start()
    {
        _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
    }

    protected void Update()
    {
        if (_queued)
        {
            lock (_queue)
            {
                //swap queue
                var tmp = _workload;
                _workload = _queue;
                _queue = tmp;
                _queued = false;
            }

            while (_workload.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }

    public void DestroyInstance()
    {
        Destroy(this);
    }

    protected void OnDestroy()
    {
        _queue = null;
        _workload = null;
    }

}
