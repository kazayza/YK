using System;
using System.Collections.Generic;
using System.Linq;

namespace YKCoatings.Services;

public enum ToastType { Success, Error, Info }

public sealed class ToastMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public ToastType Type { get; set; } = ToastType.Info;
}

public sealed class ToastService
{
    private readonly List<ToastMessage> _toasts = new();
    public IReadOnlyList<ToastMessage> Toasts => _toasts;
    public event Action? OnChanged;

    public void Show(string text, ToastType type = ToastType.Info)
    {
        var t = new ToastMessage { Text = text, Type = type };
        _toasts.Add(t);
        OnChanged?.Invoke();
        // auto-dismiss handled by component via timer
    }

    public void Success(string text) => Show(text, ToastType.Success);
    public void Error(string text) => Show(text, ToastType.Error);
    public void Info(string text) => Show(text, ToastType.Info);

    public void Remove(Guid id)
    {
        _toasts.RemoveAll(t => t.Id == id);
        OnChanged?.Invoke();
    }
}
