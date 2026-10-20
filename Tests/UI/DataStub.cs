using Dalamud.Plugin.Services;
using Dalamud.Game;
using Lumina;
using Lumina.Excel;
using Lumina.Data;
public class DataStub(GameData game) : IDataManager
{
 public ClientLanguage Language => ClientLanguage.Japanese;
 public GameData GameData => game;
 public ExcelModule Excel => game.Excel;
 public bool HasModifiedGameDataFiles => false;
 public ExcelSheet<T> GetExcelSheet<T>(ClientLanguage? language=null,string? name=null) where T:struct,IExcelRow<T> => game.GetExcelSheet<T>(Lumina.Data.Language.Japanese,name)!;
 public SubrowExcelSheet<T> GetSubrowExcelSheet<T>(ClientLanguage? language=null,string? name=null) where T:struct,IExcelSubrow<T> => game.GetSubrowExcelSheet<T>(Lumina.Data.Language.Japanese,name)!;
 public FileResource? GetFile(string path)=>game.GetFile(path);
 public T? GetFile<T>(string path) where T:FileResource => game.GetFile<T>(path);
 public Task<T> GetFileAsync<T>(string path,CancellationToken cancellationToken=default) where T:FileResource => Task.FromResult(GetFile<T>(path)!);
 public bool FileExists(string path)=>game.FileExists(path);
}

