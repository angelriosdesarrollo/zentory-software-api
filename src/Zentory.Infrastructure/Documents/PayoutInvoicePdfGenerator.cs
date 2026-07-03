using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Documents;

public sealed class PayoutInvoicePdfGenerator : IPayoutInvoicePdfGenerator
{
    public byte[] Generate(PayoutInvoicePdfModel model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("Cuenta de cobro").FontSize(20).Bold();
                    col.Item().Text(model.CompanyName).FontSize(11).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Colaborador: {model.CollaboratorName}");
                    if (!string.IsNullOrWhiteSpace(model.CollaboratorIdNumber))
                        col.Item().Text($"Identificación: {model.CollaboratorIdNumber}");
                    col.Item().Text($"Período: {model.Period}");
                    col.Item().Text($"Concepto: {model.Concept}");
                    col.Item().Text($"Fecha de emisión: {model.IssuedAt:dd/MM/yyyy}");
                    col.Item().PaddingTop(16).Text($"Valor: {model.Amount:N0} {model.Currency}").FontSize(16).Bold();
                });

                page.Footer().AlignCenter().Text("Generado con Zentory").FontSize(9).FontColor(Colors.Grey.Medium);
            });
        });

        return document.GeneratePdf();
    }
}
