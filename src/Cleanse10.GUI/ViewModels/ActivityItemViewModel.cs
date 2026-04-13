namespace Cleanse10.ViewModels;

public class ActivityItemViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isCompleted;
    private bool _isError;

    public ActivityItemViewModel(string message)
    {
        Message = message;
    }

    public string Message { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetField(ref _isCompleted, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetField(ref _isError, value);
    }
}
