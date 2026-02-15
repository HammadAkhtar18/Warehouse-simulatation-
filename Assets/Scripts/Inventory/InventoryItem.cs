using System;
using UnityEngine;

[Serializable]
public class InventoryItem
{
    [SerializeField] private string itemId;
    [SerializeField] private string itemName;

    public string ItemId => itemId;
    public string ItemName => itemName;

    public InventoryItem(string itemId, string itemName)
    {
        this.itemId = itemId;
        this.itemName = itemName;
    }
}
