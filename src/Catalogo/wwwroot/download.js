// Entrega de arquivo gerado em memória (RN-35).
//
// O PDF vem do servidor pelo circuito e **nunca toca o disco do host**: aqui ele vira um
// endereço temporário no próprio navegador, o download é disparado, e o endereço é revogado em
// seguida. É a exceção à ADR-001 que a tela de acesso já abriu — sem cadeia de build, e cabe em
// poucas linhas.
window.baixarArquivo = async (nomeDoArquivo, referenciaDoFluxo) => {
    const fluxo = await referenciaDoFluxo.arrayBuffer();
    const endereco = URL.createObjectURL(new Blob([fluxo], { type: 'application/pdf' }));

    const ancora = document.createElement('a');
    ancora.href = endereco;
    ancora.download = nomeDoArquivo ?? 'catalogo.pdf';
    ancora.style.display = 'none';

    document.body.appendChild(ancora);
    ancora.click();
    document.body.removeChild(ancora);

    // A revogação é **adiada**, e isso não é zelo: revogar no mesmo laço de eventos do clique
    // cancela o download em alguns navegadores, porque o endereço deixa de existir antes de o
    // download ser iniciado de fato. O sintoma é péssimo de diagnosticar — o arquivo simplesmente
    // não chega, sem erro em lugar nenhum.
    //
    // Um segundo é folgado para o navegador assumir a transferência; o endereço vive só nessa
    // aba e desaparece com ela de qualquer forma.
    setTimeout(() => URL.revokeObjectURL(endereco), 1000);
};
