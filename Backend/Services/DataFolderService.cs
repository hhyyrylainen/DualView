namespace Backend.Services;

public interface IDataFolderService
{
    public string GetDataFolderPath();
    public string GetDatabaseFilePath();
    public string GetLogsFolderPath();
    public void EnsureDataFoldersExist();
}
