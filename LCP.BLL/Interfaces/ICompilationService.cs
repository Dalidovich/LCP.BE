using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface ICompilationService
{
    Task<CompilationDto?> BuildAsync(bool rebuild);
    byte[]? GetData(string id);
}
