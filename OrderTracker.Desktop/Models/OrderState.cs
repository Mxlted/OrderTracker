using System;

namespace OrderTracker.Desktop.Models;

public static class OrderState
{
    public static bool IsFinal(OrderStatus status)
    {
        return status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;
    }

    public static bool IsOpen(OrderStatus status)
    {
        return !IsFinal(status);
    }

    public static bool CanMarkDelivered(OrderStatus status)
    {
        return IsOpen(status);
    }

    public static bool CanArchive(OrderStatus status)
    {
        return status == OrderStatus.Delivered;
    }

    public static bool HasPrimaryAction(OrderStatus status)
    {
        return CanMarkDelivered(status) || CanArchive(status);
    }

    public static bool CanToggleDelivered(OrderStatus status)
    {
        return CanMarkDelivered(status) || CanArchive(status);
    }

    public static bool IsOverdue(OrderStatus status, DateTime? expectedDate, DateTime today)
    {
        return IsOpen(status) && expectedDate?.Date < today.Date;
    }

    public static DateTime? GetCoherentDeliveredDate(OrderStatus status, DateTime? deliveredDate, DateTime today)
    {
        return status == OrderStatus.Delivered ? deliveredDate ?? today.Date : deliveredDate;
    }
}
