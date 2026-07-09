namespace KCert.Services;

public interface IK8sInterface<T>
{
    IAsyncEnumerable<T> ListAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<T> ListAsync(string ns, CancellationToken cancellationToken);
    Task<T> GetAsync(string ns, string name, CancellationToken tok);
    Task<T> CreateAsync(string ns, string name, T obj, CancellationToken tok);
    Task DeleteAsync(string ns, string name, CancellationToken tok);
    void Update(T source, T  target);
    Task UpdateAsync(string ns, string name, T obj, CancellationToken tok);
}
