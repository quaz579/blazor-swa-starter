namespace App.Core.Security;

public interface IFieldProtector
{
    string Protect(string plaintext, string purpose);
    string Unprotect(string envelope, string purpose);
}
