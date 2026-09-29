// Entrega de arquivo gerado em memória (RN-35).
//
// O PDF vem do servidor pelo circuito e **nunca toca o disco do host**: aqui ele vira um
// endereço temporário no próprio navegador, o download é disparado, e o endereço é revogado
// em seguida. É a exceção à ADR-001 que a tela de acesso já abriu — sem cadeia de build, e
// cabe em poucas linhas.
window.baixarArquivo = async (nomeDoArquivo, referenciaDoFluxo) => {
    const fluxo = await referenciaDoFluxo.arrayBuffer();
    const endereco = URL.createObjectURL(new Blob([fluxo], { type: 'application/pdf' }));

    const ancora = document.createElement('a');
    ancora.href = endereco;
    ancora.download = nomeDoArquivo ?? 'catalogo.pdf';
    document.body.appendChild(ancora);
    ancora.click();
    document.body.removeChild(ancora);

    URL.revokeObjectURL(endereco);
};
