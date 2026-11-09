namespace RPA.NuGet.Models
{
    /// <summary>
    /// Одна зависимость пакета, объявленная в <c>&lt;dependencies&gt;</c> его <c>.nuspec</c>.
    /// <para>
    /// <see cref="VersionRange"/> хранится строкой в том виде, в каком её объявил автор пакета
    /// (<c>8.0.0</c>, <c>[1.0.0, 2.0.0)</c>): конкретную версию подбирает менеджер при установке.
    /// </para>
    /// </summary>
    public class NuGetPackageDependency
    {
        public string Id { get; set; }

        public string VersionRange { get; set; }
    }
}
