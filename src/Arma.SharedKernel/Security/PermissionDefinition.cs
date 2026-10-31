namespace Arma.SharedKernel.Security;

/// <summary>Bir modülün tanımladığı izin. <see cref="Name"/> aynı zamanda yetkilendirme politikasının adıdır.</summary>
/// <param name="Name">Örneğin <c>contacts.read</c>.</param>
/// <param name="Group">İzin ekranında başlık olarak gösterilen modül adı.</param>
/// <param name="Description">Kullanıcıya gösterilen açıklama.</param>
public sealed record PermissionDefinition(string Name, string Group, string Description);
