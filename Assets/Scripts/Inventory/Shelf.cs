using System;
using UnityEngine;

public enum ShelfStockColor
{
    Green,
    Yellow,
    Red
}

public class Shelf : MonoBehaviour
{
    [SerializeField] private InventoryItem inventoryItem;
    [SerializeField, Min(1)] private int maxCapacity = 100;
    [SerializeField, Min(0)] private int currentStock;
    [SerializeField, Range(0f, 1f)] private float lowStockPercent = 0.2f;

    private bool wasLowStock;

    public event Action<Shelf> OnLowStockDetected;

    public InventoryItem InventoryItem => inventoryItem;
    public int MaxCapacity => maxCapacity;
    public int CurrentStock => currentStock;
    public int AvailableSpace => Mathf.Max(0, maxCapacity - currentStock);
    public bool IsFull => currentStock >= maxCapacity;
    public bool IsLowStock => currentStock <= LowStockThreshold;
    public int LowStockThreshold => Mathf.CeilToInt(maxCapacity * lowStockPercent);
    public ShelfStockColor StockColor => GetStockColor();

    public void SetInventoryItem(InventoryItem item)
    {
        inventoryItem = item;
    }

    public int AddStock(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int stockToAdd = Mathf.Min(amount, AvailableSpace);
        currentStock += stockToAdd;
        EvaluateLowStockStatus();
        return stockToAdd;
    }

    public int RemoveStock(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int stockToRemove = Mathf.Min(amount, currentStock);
        currentStock -= stockToRemove;
        EvaluateLowStockStatus();
        return stockToRemove;
    }

    public void SetStock(int amount)
    {
        currentStock = Mathf.Clamp(amount, 0, maxCapacity);
        EvaluateLowStockStatus();
    }

    public ShelfStockColor GetStockColor()
    {
        if (IsFull)
        {
            return ShelfStockColor.Green;
        }

        if (IsLowStock)
        {
            return ShelfStockColor.Red;
        }

        return ShelfStockColor.Yellow;
    }

    private void Awake()
    {
        currentStock = Mathf.Clamp(currentStock, 0, maxCapacity);
        wasLowStock = IsLowStock;
    }

    private void OnValidate()
    {
        maxCapacity = Mathf.Max(1, maxCapacity);
        currentStock = Mathf.Clamp(currentStock, 0, maxCapacity);
        lowStockPercent = Mathf.Clamp01(lowStockPercent);
        wasLowStock = IsLowStock;
    }

    private void EvaluateLowStockStatus()
    {
        bool isLowStockNow = IsLowStock;

        if (isLowStockNow && !wasLowStock)
        {
            OnLowStockDetected?.Invoke(this);
        }

        wasLowStock = isLowStockNow;
    }
}
