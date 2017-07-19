using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Funadesp.SisBolsas.Domain.Context;
using Funadesp.SisBolsas.Domain.Entities.Enuns;
using Funadesp.SisBolsas.Domain.Misc;
using Lib.Extensions;
using Lib.Util;
using RazorEngine;
using RazorEngine.Templating;
using RazorEngine.Configuration;
using BaseDomain.Misc;
using BaseDomain.BaseEntities;
using System.Text;
using System.Threading;

namespace Funadesp.SisBolsas.Domain.Entities
{
    public class ProjetoCapacitacaoBo : ProjetoBo
    {
        private Projeto _projetoOriginalNoBd;

        public ProjetoCapacitacaoBo(DbContext contexto, UnitOfWork<FunadespContext> uow = null)
            : base(contexto, uow)
        {
        }

        private async Task<Projeto> GetProjetoOriginalNoBd(int idProjeto) =>
            _projetoOriginalNoBd ?? (_projetoOriginalNoBd = await GetSingleById(idProjeto, inc => inc.Coordenador));


        public async Task<Projeto> IesSalvaProjeto(Projeto projeto, Pessoa usrRegistro, List<AreaCnpqProjeto> areasCnpq, List<SubAreaCnpqProjeto> subAreasCnpq, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            try
            {
                if (projeto.Status != StatusProjeto.RascunhoIes                    &&
                    projeto.Status != StatusProjeto.SubmetidoProSolicitantePelaIes &&
                    projeto.Status != StatusProjeto.SubmetidoPraFunadespPelaIes    )
                {
                    projeto.Status  = StatusProjeto.RascunhoIes;
                }

                switch (projeto.Status)
                {
                    case StatusProjeto.SubmetidoProSolicitantePelaIes:
                        projeto.Status = StatusProjeto.RascunhoIes;
                        await SalvarComoRascunhoIes(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
                        if(projeto.IsValid()) await SubmterRascunhoProjetoCapacitacaoDaIesProSolicitante(projeto);
                        break;

                    case StatusProjeto.SubmetidoPraFunadespPelaIes:
                        // projeto = await SubmterRascunhoProjetoCapacitacaoPraFunadesp(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
                        break;

                    case StatusProjeto.RascunhoIes:
                    default:
                        projeto = await SalvarComoRascunhoIes(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
                        break;
                }

                return projeto;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


        public async Task<Projeto> SolicitanteSalvaProjeto(Projeto projeto, Pessoa usrRegistro, List<AreaCnpqProjeto> areasCnpq, List<SubAreaCnpqProjeto> subAreasCnpq, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            try
            {
                var projetoId = projeto.Id;
                var projetoNoBd = await GetProjetoOriginalNoBd(projetoId);

                // No salvamento em si, essas informações não são necessárias, mas para as validações, sim.
                projeto.DataFimProjeto              = projetoNoBd.DataFimProjeto;
                projeto.DataInicioProjeto           = projetoNoBd.DataInicioProjeto;
                projeto.IniVigenciaBolsaCoordenador = projetoNoBd.IniVigenciaBolsaCoordenador;
                projeto.FimVigenciaBolsaCoordenador = projetoNoBd.FimVigenciaBolsaCoordenador;

                // O solicitante continua fazendo alterações no projeto
                if (projetoNoBd.Status == StatusProjeto.SubmetidoProSolicitantePelaIes && projeto.Status == projetoNoBd.Status)
                    return await SalvarComoRascunhoSolicitante(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);

                // Solicitante está envidando de volta pra IES
                if (projetoNoBd.Status == StatusProjeto.SubmetidoProSolicitantePelaIes && projeto.Status == StatusProjeto.SubmetidoPraIesPeloSolicitante)
                {
                    await SalvarComoRascunhoSolicitante(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
                    if (projeto.IsValid())
                    {
                        await SubmterRascunhoProjetoCapacitacaoDoSolicitantePraIes(projeto);
                        return projeto;
                    }
                }

                projetoNoBd.ValidationResult.Add("StatusIncondizendoProProjeto",
                    "Algo estranho aconteceu. O projeto está definido com status {0} que é incondizente com a ação que se está tentando executar (solicitante salvar projeto)"
                        .ToFormat(projeto.Status.ToDescription())
                );

                return projetoNoBd;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


        public async Task<Projeto> SalvarComoRascunhoIes(Projeto projeto, Pessoa usrRegistro, List<AreaCnpqProjeto> areasCnpq,
            List<SubAreaCnpqProjeto> subAreasCnpq, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            projeto.ProjetoCapacitacaoPodeSerSalvoComoRascunhoIesValidation(Uow);

            return await SalvarComoRascunho(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
        }


        public async Task<Projeto> SalvarComoRascunhoSolicitante(Projeto projeto, Pessoa usrRegistro, List<AreaCnpqProjeto> areasCnpq,
            List<SubAreaCnpqProjeto> subAreasCnpq, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            projeto.ProjetoCapacitacaoPodeSerSalvoComoRascunhoSolicitanteValidation(Uow);

            return await SalvarComoRascunho(projeto, usrRegistro, areasCnpq, subAreasCnpq, pdfCartaAceitacao, pdfAnteProjeto);
        }


        public async Task<Projeto> SalvarComoRascunho(Projeto projeto, Pessoa usrRegistro, List<AreaCnpqProjeto> areasCnpq,
            List<SubAreaCnpqProjeto> subAreasCnpq, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            try
            {
                var path2SaveFiles = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, new AppSettings().PathDocsProjetos);

                if (projeto.IsInvalid())
                    return projeto;

                await DefineInformacaoSobreOEnvioDosArquivos(projeto, pdfCartaAceitacao, pdfAnteProjeto);

                if (projeto.Id == 0) // no caso de ser o cadastro de um novo projeto
                {
                    projeto.StatusAvaliacaoTermoCompromissoCoordenador = StsAvaliacaoTermoCompromisso.AguardandoAvaliacao;
                    projeto.Bolsistas.ToList().ForEach(bolsista => bolsista.StatusAvaliacaoTermoCompromisso = StsAvaliacaoTermoCompromisso.AguardandoAvaliacao);
                }

                switch (projeto.Status)
                {
                    case StatusProjeto.SubmetidoProSolicitantePelaIes:
                        await base.UpdateSpecificPropertiesAsync(projeto, true, false, prop =>
                            prop.Nome,
                            prop => prop.NomeCurso,
                            prop => prop.IesLocalExecucaoId,
                            prop => prop.PossuiCartaDeAceitacaoCadastrada,
                            prop => prop.DataEnvioCartaDeAceitacao,
                            prop => prop.PossuiAnteProjetoCadastrado,
                            prop => prop.DataEnvioAnteProjeto
                        ); break;
                    case StatusProjeto.SubmetidoPraIesPeloSolicitante:
                        await base.UpdateSpecificPropertiesAsync(projeto, true, false, prop =>
                            prop.Nome,
                            prop => prop.NomeCurso,
                            prop => prop.IesLocalExecucaoId,
                            prop => prop.PossuiCartaDeAceitacaoCadastrada,
                            prop => prop.DataEnvioCartaDeAceitacao,
                            prop => prop.PossuiAnteProjetoCadastrado,
                            prop => prop.DataEnvioAnteProjeto
                        ); break;

                    case StatusProjeto.RascunhoIes:                                  case StatusProjeto.EncerradoPeloSolicitante:
                    case StatusProjeto.SubmetidoPraFunadespPelaIes:                  case StatusProjeto.ReprovadoPelaFunadesp:
                    case StatusProjeto.SubmetidoDaFunadespPraAjustesPorParteDaIes:   case StatusProjeto.EncerradoPelaIes:
                    case StatusProjeto.ParecerFavoravelSubmetidoAoDiretorDaFunadesp: case StatusProjeto.AprovadoPelaFunadesp:
                    default:
                        await base.Save(projeto); break;
                }

                // await SalvarAlteracoesNosBolsistasRelacionados(projeto);
                await SalvarAlteracoesNasAreasRelacionadas(projeto.Id, areasCnpq, subAreasCnpq);

                if (pdfCartaAceitacao.IsNotNull())
                    File.WriteAllBytes("{0}{1}_CartaAceitacao.pdf".ToFormat(path2SaveFiles, projeto.Id), pdfCartaAceitacao.ToArray());

                if (pdfAnteProjeto.IsNotNull())
                    File.WriteAllBytes("{0}{1}_AnteProjeto.pdf".ToFormat(path2SaveFiles, projeto.Id), pdfAnteProjeto.ToArray());

                return projeto;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task DefineInformacaoSobreOEnvioDosArquivos(Projeto projeto, byte[] pdfCartaAceitacao, byte[] pdfAnteProjeto)
        {
            projeto.PossuiAnteProjetoCadastrado = pdfAnteProjeto.IsNotNull();
            if (pdfAnteProjeto.IsNotNull()) projeto.DataEnvioAnteProjeto = DateTime.Now;

            projeto.PossuiCartaDeAceitacaoCadastrada = pdfCartaAceitacao.IsNotNull();
            if (pdfCartaAceitacao.IsNotNull()) projeto.DataEnvioCartaDeAceitacao = DateTime.Now;

            // Quando da edição, é necessário verificar se será necessário atualizar os campos "PossuiCartaDeAceitacaoCadastrada"
            if (projeto.Id > 0) // e "PossuiAnteProjetoCadastrado" de acordo com o envio ou não desses documentos
            {
                var projetoId = projeto.Id;
                var projetoNoBd = await GetProjetoOriginalNoBd(projetoId);

                projeto.PossuiCartaDeAceitacaoCadastrada = projetoNoBd.PossuiCartaDeAceitacaoCadastrada || !projetoNoBd.PossuiCartaDeAceitacaoCadastrada && projeto.PossuiCartaDeAceitacaoCadastrada;

                projeto.PossuiAnteProjetoCadastrado = projetoNoBd.PossuiAnteProjetoCadastrado || !projetoNoBd.PossuiAnteProjetoCadastrado && projeto.PossuiAnteProjetoCadastrado;
            }
        }

        public string GetHtmlTermoCompromissoSolicitante(Projeto projeto, Pessoa solicitante)
        {
            projeto.UsuarioPodeVisualizarOTermoDeCompromissoValidation(solicitante);

            if (projeto.IsValid())
            {
                var strTemplateTermoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, new AppSettings().PathTemplatesProjetosCapacitacao, "SolicitanteBolsista.cshtml");
                return GetHtmlFromRazorTemplate(strTemplateTermoPath, projeto);
            }

            if (projeto.ValidationResult.Erros.Count == 1)
                return projeto.ValidationResult.Erros.First().Value;

            var sb = new StringBuilder().Append("<ul>");
            projeto.ValidationResult.Erros.ToList().ForEach(x => sb.AppendFormat("<li>{0}</li>", x.Value));
            return sb.Append("</ul>").ToString();
        }

        public async Task<Projeto> GetProjetoParaExibicaoDoTermoDeCompromisso(int projetoId)
        {
            return await GetSingleById(projetoId,
                inc => inc.IesFinanciadora,
                inc => inc.Coordenador.UfEndereco,
                inc => inc.Coordenador.EstadoCivil,
                inc => inc.Coordenador.PaisNascimento,
                inc => inc.ValorBolsaCoordenador.ModalidadeBolsa,
                inc => inc.Bolsistas.Select(bolsista => bolsista.Pessoa.UfEndereco),
                inc => inc.Bolsistas.Select(bolsista => bolsista.Pessoa.EstadoCivil),
                inc => inc.Bolsistas.Select(bolsista => bolsista.ValorBolsa.ModalidadeBolsa)
            );
        }

        public async Task<ValidationResult> AvaliarTermoDecompromisso(int idProjeto, bool aceito)
        {
            var bo                  = Uow.GetProjetoCapacitacaoBo();
            var iprincipalUsrLogado = Thread.CurrentPrincipal as CustomPrincipal;
            var projeto             = await bo.GetProjetoParaExibicaoDoTermoDeCompromisso(idProjeto);
            var usrLogado           = await Uow.GetPessoaBo().GetSingleById(iprincipalUsrLogado.Id);
            projeto.UsuarioPodeAvaliarOTermoDeCompromissoValidation(usrLogado, Uow);

            if (projeto.IsInvalid()) return projeto.ValidationResult;

            projeto.DataAvaliacaoTermoCompromissoPeloCoordenador   = DateTime.Now;
            if (aceito) {
                projeto.Status                                     = StatusProjeto.SubmetidoPraFunadespPelaIes;
                projeto.StatusAvaliacaoTermoCompromissoCoordenador = StsAvaliacaoTermoCompromisso.Aceito;
            } else {
                projeto.Status                                     = StatusProjeto.EncerradoPeloSolicitante;
                projeto.StatusAvaliacaoTermoCompromissoCoordenador = StsAvaliacaoTermoCompromisso.Recusado;
            }

            await UpdateSpecificPropertiesAsync(
                projeto,                                             /*saveChanges:*/true, /*validateOnSaveEnabled: */false,
                x => x.Status,
                x => x.StatusAvaliacaoTermoCompromissoCoordenador,
                x => x.DataAvaliacaoTermoCompromissoPeloCoordenador
            );

            return new ValidationResult();
        }

        private string GetHtmlFromRazorTemplate<T>(string templateFilePath, T model)
        {
            Engine.Razor = RazorEngineService.Create(new TemplateServiceConfiguration { Debug = true });

            var template = File.ReadAllText(templateFilePath);
            new LoadedTemplateSource(template, templateFilePath);
            return Engine.Razor.RunCompile(new LoadedTemplateSource(template, templateFilePath), "templateKey", typeof(T), model);
        }

        #region Submissões do projeto
        public async Task<Projeto> SubmterRascunhoProjetoCapacitacaoDaIesProSolicitante(Projeto projeto)
        {
            if (projeto.RascunhoIesProjetoCapacitacaoIesPodeSerSubmetidoAoSolicitanteValidation().IsInvalid()) return projeto;

            await AlteraStatusProjeto(projeto, StatusProjeto.SubmetidoProSolicitantePelaIes);

            var coordenadorProjeto = projeto.Coordenador ?? (await GetProjetoOriginalNoBd(projeto.Id)).Coordenador;
            Emails.ProjetoCapacitacaoRascunhoSubmetidoAoSolicitante(coordenadorProjeto.EmailPessoal, coordenadorProjeto.Nome);

            return projeto;
        }

        public async Task<Projeto> SubmterRascunhoProjetoCapacitacaoDoSolicitantePraIes(Projeto projeto)
        {
            if (projeto.ProjetoCapacitacaoPodeSerSubmetidoPraIesPeloSolicitanteValidation(Uow).IsInvalid()) return projeto;

            return await AlteraStatusProjeto(projeto, StatusProjeto.SubmetidoPraIesPeloSolicitante);
        }

        public async Task<Projeto> SubmterProjetoCapacitacaoDaIesPraFunadesp(Projeto projeto)
        {
            if (projeto.ProjetoCapacitacaoPodeSerSubmetidoPraFunadespPelaIesValidation(Uow).IsInvalid()) return projeto;

            await AlteraStatusProjeto(projeto, StatusProjeto.SubmetidoPraFunadespPelaIes);

            // Enviar email para os bolsistas avisando que devem avaliar o termo de aceite do projeto
            /*
            if (!projeto.CoordenadorVoluntario)
            {
                projeto.Coordenador = await Uow.GetPessoaBo().GetSingleById(projeto.CoordenadorId);
                Emails.ProjetoCapacitacaoRequerAvaliacaoDoTermoDeAceitePorParteDoBolsista(projeto.Coordenador.EmailPessoal, projeto.Coordenador.Nome, projeto.Nome);
            }

            projeto.Bolsistas = Uow.GetBolsistaProjetoBo().GetWhere(db => db.ProjetoId == projeto.Id, inc => inc.Pessoa).ToList();
            projeto.Bolsistas.ToList().ForEach(bolsista =>
                Emails.ProjetoCapacitacaoRequerAvaliacaoDoTermoDeAceitePorParteDoBolsista(bolsista.Pessoa.EmailPessoal, bolsista.Pessoa.Nome, projeto.Nome)
            );*/

            return projeto;
        }


        public async Task<Projeto> AlteraStatusProjeto(Projeto projeto, StatusProjeto novoStatus)
        {
            projeto.Status = novoStatus;
            try
            {
                await UpdateSpecificPropertiesAsync(
                    projeto,
                    true,  // saveChanges
                    false, // validateOnSaveEnabled
                    prop => prop.Status
                );

                return projeto;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion

        public async Task<Projeto> IesReprovaESubmeteDeVoltaProSolicitante(int idProjeto, string justificativaReprovacao)
        {
            var projeto = await GetSingleById(idProjeto);

            projeto.PodeSerReprovadoPelaIesValidation();
            if (justificativaReprovacao.IsNullOrEmpty())
                projeto.ValidationResult.Add("Projeto_ReprovacaoSemJustificativa", "Não é possível reprovar o projeto sem informar uma justificativa.");

            if (projeto.IsInvalid())
                return projeto;

            projeto.Status = StatusProjeto.SubmetidoProSolicitantePelaIes;
            await Save(projeto);

            await Uow.GetMensagemProjetoBo().SalvarNovaMensagem(projeto, justificativaReprovacao);

            return projeto;
        }


        public async Task DiretoriaTecnicaSolicitaAjuesteNaForma(int projetoId, string justificativa)
        {
            await MovimentarProjeto(projetoId, StatusProjeto.SubmetidoDaFunadespPraAjustesPorParteDaIes, justificativa);
        }

        public async Task DiretoriaTecnicaSolicitaAjuesteNoConteudo(int projetoId, string justificativa)
        {
            await MovimentarProjeto(projetoId, StatusProjeto.SubmetidoDaFunadespProSolicitantePraAjustes, justificativa);
        }

        public async Task DiretoriaTecnicaEmiteParecerFavoravel(int projetoId, string justificativa)
        {
            await MovimentarProjeto(projetoId, StatusProjeto.ParecerFavoravelSubmetidoAoDiretorDaFunadesp, justificativa);
        }

        public async Task DiretoriaTecnicaEmiteParecerDesfavoravel(int projetoId, string justificativa)
        {
            await MovimentarProjeto(projetoId, StatusProjeto.ParecerDesfavoravelSubmetidoAoDiretorDaFunadesp, justificativa);
        }

        public async Task MovimentarProjeto(int idProjeto, StatusProjeto status, string justificativa = null)
        {
            if (idProjeto <= 0) throw new ArgumentException($"idProjeto inválido. Valor informado » {idProjeto}");
            var projeto = await GetSingleByIdAsTraking(idProjeto);
            await MovimentarProjeto(projeto, status, justificativa);
        }

        public async Task MovimentarProjeto(Projeto projeto, StatusProjeto status, string justificativa = null)
        {
            if (projeto.Status == status) return;

            projeto.Status = status;

            var usuarioLogado = Thread.CurrentPrincipal as CustomPrincipal;

            await Save(projeto);

            await Uow.GetMensagemProjetoBo().SalvarNovaMensagem(projeto, justificativa);

            switch (status)
            {
                case StatusProjeto.RascunhoIes:
                    break;
                case StatusProjeto.SubmetidoProSolicitantePelaIes:
                    break;
                case StatusProjeto.EncerradoPeloSolicitante:
                    break;
                case StatusProjeto.SubmetidoPraIesPeloSolicitante:
                    break;
                case StatusProjeto.SubmetidoPraFunadespPelaIes:
                    break;
                case StatusProjeto.SubmetidoDaFunadespPraAjustesPorParteDaIes:
                    break;
                case StatusProjeto.EncerradoPelaIes:
                    break;
                case StatusProjeto.SubmetidoDaFunadespProSolicitantePraAjustes:
                    break;
                case StatusProjeto.SubmetidoDoSolicitantePraFunadesp:
                    break;
                case StatusProjeto.AguardandoAvaliacaoTermoCompromisso:
                    break;
                case StatusProjeto.ParecerFavoravelSubmetidoAoDiretorDaFunadesp:
                    break;
                case StatusProjeto.ParecerDesfavoravelSubmetidoAoDiretorDaFunadesp:
                    break;
                case StatusProjeto.AprovadoPelaFunadesp:
                    break;
                case StatusProjeto.ReprovadoPelaFunadesp:
                    break;
                case StatusProjeto.SubmetidoDoDiretorPraDirTecnicaFunadesp:
                    break;
                case StatusProjeto.SubmetidoDoDiretorPraDirTecPraAjustesDaDirTec:
                    break;
                default:
                    break;
            }
        }



        public bool PessoaEhASolicitanteDoProjeto(Projeto projeto, int pessoaId)
        {
            return projeto.CoordenadorId == pessoaId;
        }

        public bool TermosDeAceiteDisponibilizados(Projeto projeto)
        {
            return projeto.Status != StatusProjeto.RascunhoIes;
        }

        public async Task<Projeto> ExcluirProjetoRascunho(Projeto projeto)
        {
            projeto = await GetSingleById(projeto.Id);

            projeto.ProjetoPodeSerExcluido(Uow);

            if (projeto.ValidationResult.IsInvalid())
                return projeto;

            await ExcluirAreasESubAreasCnpqDoProjeto(projeto);
            await Delete(projeto);

            ExcluirArquivosRelacionadosAoProjeto(projeto);

            return projeto;
        }

        public async Task ExcluirAreasESubAreasCnpqDoProjeto(Projeto projeto)
        {
            await Uow.GetAreaCnpqProjetoBo   ().ExcluirDoProjetoPeloProjetoId(projeto.Id);
            await Uow.GetSubAreaCnpqProjetoBo().ExcluirDoProjetoPeloProjetoId(projeto.Id);

        }

        public void ExcluirArquivosRelacionadosAoProjeto(Projeto projeto)
        {
            var rootPath  = AppDomain.CurrentDomain.BaseDirectory;
            var filesPath = "{0}\\{1}\\".ToFormat(rootPath, new AppSettings().PathDocsProjetos);

            var pathAnteProjeto    = "{0}{1}_AnteProjeto.pdf"   .ToFormat(filesPath, projeto.Id);
            var pathCartaAceitacao = "{0}{1}_CartaAceitacao.pdf".ToFormat(filesPath, projeto.Id);

            FileExtensions.DeleteFilesIfExists(pathAnteProjeto, pathCartaAceitacao);
        }
    }
}