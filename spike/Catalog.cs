namespace Spike;

public sealed record Product(string Name, string Description, decimal Price, string PriceLabel, string PhotoPath);

public sealed record Category(string Name, IReadOnlyList<Product> Products);

/// <summary>
/// Dados fixos do spike. Os nomes e descrições reproduzem a variação de comprimento
/// observada no catálogo de referência — do mais curto ao mais longo — porque é essa
/// variação que decide se a célula da grade aguenta o conteúdo real.
/// </summary>
public static class Catalog
{
    private const string DefaultPriceLabel = "PREÇO";

    public static IReadOnlyList<Category> Build(IReadOnlyList<string> photos)
    {
        var photo = new Queue<string>(photos);

        return new[]
        {
            new Category("Impressoras", new[]
            {
                Product("Impressora Multifuncional Epson L3250", "Tanque de tinta, Wi-Fi Direct, impressão, cópia e digitalização", 1349.90m, photo),
                Product("Impressora Brother DCP-1602", "Laser monocromática, 21 ppm", 1189.00m, photo),
                Product("Impressora HP Smart Tank 581", string.Empty, 1599.00m, photo),
                Product("Multifuncional Canon G3110", "Tanque de tinta integrado, conexão sem fio, bandeja para 100 folhas, indicada para volume médio de impressão em escritório", 1249.00m, photo)
            }),
            new Category("Energia & Proteção", new[]
            {
                Product("Nobreak SMS Station II 1200VA", "Bivolt automático, 6 tomadas, bateria interna selada", 899.00m, photo),
                Product("Filtro de Linha 6 Tomadas", "Com proteção contra surto", 79.90m, photo)
            }),
            new Category("Tintas & Suprimentos", new[]
            {
                Product("Refil de Tinta EPSON 544 Original", "Frasco de 65 ml", 64.90m, photo, "PREÇO/UND"),
                Product("Toner Brother TN-1060", "Compatível com as séries DCP-1602 e HL-1202, rendimento aproximado de 1.000 páginas", 89.90m, photo),
                Product("Papel Sulfite A4 75g", "Resma com 500 folhas", 32.90m, photo),
                Product("Cartucho HP 664XL Preto", string.Empty, 129.90m, photo)
            }),
            new Category("Softwares & Licenças", new[]
            {
                Product("Windows 11 Education Pro", "Licenciamento e atualização de sistema operacional, verba de custeio, destinado a instituições de ensino com documentação fiscal completa", 419.00m, photo),
                Product("Microsoft Office 2021", "Licença vitalícia para um dispositivo", 649.00m, photo)
            }),
            new Category("Computadores & Componentes", new[]
            {
                Product("Notebook Positivo Vision R15M", "Ryzen 3-7330U, 8GB, SSD 256GB, Wi-Fi 6, 15\" IPS, Linux – Preto", 2399.00m, photo),
                Product("SSD Kingston NV2 500GB", "NVMe PCIe 4.0, leitura até 3.500 MB/s", 289.00m, photo),
                Product("Memória DDR4 8GB 3200MHz", string.Empty, 159.00m, photo),
                Product("Monitor LG 24MK430H", "24 polegadas, IPS, Full HD, 75Hz, FreeSync, entradas HDMI e VGA, suporte com ajuste de inclinação", 749.00m, photo),
                Product("Gabinete Gamer ATX", "Lateral em vidro temperado, três coolers RGB inclusos", 329.00m, photo),
                Product("Fonte 500W 80 Plus", "Com PFC ativo", 279.00m, photo),
                Product("Placa-Mãe ASUS Prime H610M", "Socket LGA1700, DDR4, M.2, HDMI e VGA, formato micro-ATX", 699.00m, photo),
                Product("Processador Intel Core i5-12400F", "Seis núcleos, doze threads, 2.5 GHz base", 1099.00m, photo)
            }),
            new Category("Redes & Cabeamento", new[]
            {
                Product("Cabo de Rede CAT6", "Bobina 100 metros, homologado Anatel", 389.00m, photo),
                Product("Roteador TP-Link Archer C6", "AC1200, dual band, quatro antenas externas", 249.00m, photo),
                Product("Switch 8 Portas Gigabit", string.Empty, 189.00m, photo),
                Product("Patch Cord 2,5m", "Certificado, azul", 19.90m, photo),
                Product("Conector RJ45 CAT6", "Pacote com 100 unidades, banhado a ouro, compatível com cabo rígido e flexível", 89.90m, photo, "PREÇO/UND")
            }),
            new Category("Periféricos & Acessórios", new[]
            {
                Product("Mouse Pad 23x18cm", string.Empty, 14.90m, photo),
                Product("Teclado ABNT2 USB", "Membrana, resistente a respingos", 59.90m, photo),
                Product("Mouse Óptico USB 1000dpi", string.Empty, 29.90m, photo),
                Product("Headset com Microfone", "Conexão P2, haste ajustável, almofadas em espuma", 89.90m, photo),
                Product("Webcam Full HD 1080p", "Microfone embutido com redução de ruído, foco automático, clipe universal para monitor e tripé", 179.00m, photo),
                Product("Hub USB 3.0 4 Portas", string.Empty, 49.90m, photo),
                Product("Suporte para Notebook", "Alumínio, altura ajustável em seis níveis", 119.00m, photo),
                Product("Estabilizador 300VA", "Bivolt, quatro tomadas", 189.00m, photo),
                Product("Adaptador HDMI para VGA", "Com saída de áudio P2", 39.90m, photo),
                Product("Pen Drive 64GB USB 3.0", string.Empty, 44.90m, photo),
                Product("Caixa de Som 2.0", "Alimentação USB, controle de volume no cabo, potência de 6W RMS", 79.90m, photo)
            })
        };
    }

    private static Product Product(
        string name,
        string description,
        decimal price,
        Queue<string> photos,
        string priceLabel = DefaultPriceLabel) =>
        new(name, description, price, priceLabel, photos.Dequeue());
}
