namespace LCP.BLL.DTOs;

public record HashedPasswordDto(string PasswordHash, string PasswordSalt);
