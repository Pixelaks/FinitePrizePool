using DealEngine;
using UnityEngine;

public class UnityJsonSerializer : IJsonSerializer
{
    public string ToJson<T>(T obj) { return JsonUtility.ToJson(obj); }
    public T FromJson<T>(string json) { return JsonUtility.FromJson<T>(json); }
}