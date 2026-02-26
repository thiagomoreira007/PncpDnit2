using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

class Program
{
    static void Main()
    {
        var options = new ChromeOptions();
        // options.AddArgument("--headless");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--start-maximized");

        using var driver = new ChromeDriver(options);
        WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

        string pasta = @"C:\Users\thiago.moreira\Documents\pastadopncp";
        Directory.CreateDirectory(pasta);
        
        string statusSelecionado = "Em Julgamento_Propostas Encerradas";
        string caminhoCsv = Path.Combine(pasta, $"DNIT_{statusSelecionado}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("Local;UnidadeCompradora;Orgao;Modalidade;AmparoLegal;Tipo;ModoDisputa;RegistroPreco;FonteOrcamentaria;DataDivulgacao;Situacao;InicioPropostas;FimPropostas;IdPNCP;Objeto;InformacaoComplementar;ValorEstimado;UrlEdital");

        try
        {
            Console.WriteLine($"🔍 Iniciando extração de editais do DNIT com status: {statusSelecionado}...");

            driver.Navigate().GoToUrl("https://pncp.gov.br/app/editais");
            Thread.Sleep(5000);

            var jsExecutor = (IJavaScriptExecutor)driver;

            // --- PASSO 1: SELECIONAR O STATUS ---
            Console.WriteLine("\n🔎 Localizando opção de status...");
            
            bool statusEncontrado = false;
            
            try
            {
                var elementosJulgamento = driver.FindElements(By.XPath("//*[contains(text(), 'Em Julgamento') or contains(text(), 'Propostas Encerradas')]"));
                
                foreach (var el in elementosJulgamento)
                {
                    string textoCompleto = el.Text ?? "";
                    Console.WriteLine($"   Elemento encontrado: {textoCompleto}");
                    
                    if (textoCompleto.Contains("Em Julgamento") || textoCompleto.Contains("Propostas Encerradas"))
                    {
                        Console.WriteLine($"   ✅ Opção relacionada encontrada: {textoCompleto}");
                        
                        jsExecutor.ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", el);
                        Thread.Sleep(2000);
                        
                        try
                        {
                            el.Click();
                            Console.WriteLine("   ✅ Clique realizado com sucesso!");
                            statusEncontrado = true;
                            Thread.Sleep(2000);
                            break;
                        }
                        catch
                        {
                            try
                            {
                                jsExecutor.ExecuteScript("arguments[0].click();", el);
                                Console.WriteLine("   ✅ Clique via JavaScript realizado!");
                                statusEncontrado = true;
                                Thread.Sleep(2000);
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"   ❌ Erro ao clicar: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Erro na busca por texto: {ex.Message}");
            }

            if (!statusEncontrado)
            {
                Console.WriteLine("\n❌ NÃO FOI POSSÍVEL ENCONTRAR O STATUS 'Em Julgamento/Propostas Encerradas'");
                return;
            }

            Console.WriteLine("\n✅ Status selecionado com sucesso! Continuando com a pesquisa...");
            Thread.Sleep(2000);

            // --- PASSO 2: Selecionar órgão DNIT ---
            Console.WriteLine("\n🔎 Localizando campo Órgãos...");
            IWebElement campoOrgaos = null;
            var todosNgSelects = driver.FindElements(By.CssSelector("ng-select"));

            foreach (var ng in todosNgSelects)
            {
                try
                {
                    var label = ng.FindElement(By.XPath("./preceding::label[contains(text(), 'Órgãos')]"));
                    if (label != null)
                    {
                        campoOrgaos = ng.FindElement(By.CssSelector("input[type='text']"));
                        Console.WriteLine("   ✅ Campo Órgãos encontrado!");
                        break;
                    }
                }
                catch { }
            }

            if (campoOrgaos == null)
            {
                Console.WriteLine("❌ Campo Órgãos não encontrado!");
                return;
            }

            campoOrgaos.Click();
            campoOrgaos.Clear();
            Thread.Sleep(500);

            jsExecutor.ExecuteScript("arguments[0].value='DNIT'; arguments[0].dispatchEvent(new Event('input'));", campoOrgaos);
            Thread.Sleep(1000);

            var opcoesDNIT = driver.FindElements(By.CssSelector(".ng-option"))
                                   .Where(o => o.Text != null && o.Text.Contains("DNIT") && o.Displayed).ToList();

            int countDNIT = 0;
            foreach (var opcao in opcoesDNIT)
            {
                try
                {
                    opcao.Click();
                    countDNIT++;
                    Thread.Sleep(300);
                    Console.WriteLine($"   ✅ Opção {countDNIT} selecionada: {opcao.Text}");
                }
                catch { }
            }

            Console.WriteLine($"   Total de {countDNIT} opções de DNIT selecionadas.");

            // --- PASSO 3: Clicar no botão PESQUISAR ---
            var todosBotoes = driver.FindElements(By.TagName("button"));
            IWebElement botaoPesquisar = null;
            foreach (var btn in todosBotoes)
            {
                if (btn.Text != null && btn.Text.ToUpper().Contains("PESQUISAR"))
                {
                    botaoPesquisar = btn;
                    break;
                }
            }

            if (botaoPesquisar != null)
            {
                jsExecutor.ExecuteScript("arguments[0].scrollIntoView(true);", botaoPesquisar);
                Thread.Sleep(500);
                jsExecutor.ExecuteScript("arguments[0].click();", botaoPesquisar);
                Console.WriteLine("   ✅ Pesquisar clicado!");
            }
            else
            {
                Console.WriteLine("   ⚠️ Botão PESQUISAR não encontrado");
            }

            Thread.Sleep(5000);

            // --- PASSO 4: SELECIONAR 100 ITENS POR PÁGINA ---
            Console.WriteLine("\n🔎 Procurando dropdown de itens por página...");

            IWebElement dropdownItens = null;
            bool dropdownEncontrado = false;
            int tentativasDropdown = 0;
            int maxTentativasDropdown = 5;

            WebDriverWait waitResultados = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

            while (!dropdownEncontrado && tentativasDropdown < maxTentativasDropdown)
            {
                try
                {
                    tentativasDropdown++;
                    Console.WriteLine($"   🔄 Tentativa {tentativasDropdown} de localizar dropdown...");

                    try
                    {
                        IWebElement dropdownAguardado = waitResultados.Until(drv => 
                        {
                            var elementos = drv.FindElements(By.CssSelector("ng-select"));
                            foreach (var el in elementos)
                            {
                                string texto = el.Text ?? "";
                                if (texto.Contains("10") || texto.Contains("20") || texto.Contains("50") || texto.Contains("100"))
                                {
                                    return el;
                                }
                            }
                            return null;
                        });

                        if (dropdownAguardado != null)
                        {
                            dropdownItens = dropdownAguardado;
                            dropdownEncontrado = true;
                            Console.WriteLine($"   ✅ Dropdown encontrado via Wait explícito: '{dropdownItens.Text}'");
                            break;
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        Console.WriteLine($"   ⏳ Timeout no wait explícito.");
                    }

                    if (!dropdownEncontrado)
                    {
                        var ngSelectsAtuais = driver.FindElements(By.CssSelector("ng-select"));
                        Console.WriteLine($"      Busca direta: {ngSelectsAtuais.Count} elementos ng-select.");

                        foreach (var ng in ngSelectsAtuais)
                        {
                            try
                            {
                                string texto = ng.Text ?? "";
                                if (texto.Contains("10") || texto.Contains("20") || texto.Contains("50") || texto.Contains("100"))
                                {
                                    dropdownItens = ng;
                                    dropdownEncontrado = true;
                                    Console.WriteLine($"   ✅ Dropdown encontrado via busca direta: '{texto}'");
                                    break;
                                }
                            }
                            catch (StaleElementReferenceException)
                            {
                                continue;
                            }
                        }
                    }

                    if (!dropdownEncontrado)
                    {
                        Console.WriteLine($"   ⚠️ Tentativa {tentativasDropdown} falhou. Aguardando...");
                        Thread.Sleep(2000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Erro na tentativa {tentativasDropdown}: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }

            if (dropdownItens != null)
            {
                try
                {
                    Console.WriteLine("   📜 Rolando até o dropdown...");
                    jsExecutor.ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", dropdownItens);
                    Thread.Sleep(1000);

                    Console.WriteLine("   🔽 Abrindo dropdown...");
                    try
                    {
                        dropdownItens.Click();
                        Console.WriteLine("   ✅ Dropdown clicado.");
                    }
                    catch (ElementClickInterceptedException)
                    {
                        jsExecutor.ExecuteScript("arguments[0].click();", dropdownItens);
                        Console.WriteLine("   ✅ Clique via JavaScript no dropdown.");
                    }

                    Thread.Sleep(2000);

                    Console.WriteLine("   🔎 Procurando opção '100'...");
                    bool opcao100Selecionada = false;
                    int tentativasOpcao = 0;
                    int maxTentativasOpcao = 3;

                    while (!opcao100Selecionada && tentativasOpcao < maxTentativasOpcao)
                    {
                        tentativasOpcao++;
                        try
                        {
                            var opcoesDropdown = driver.FindElements(By.CssSelector(".ng-option, .ng-option-label, [role='option']"));
                            Console.WriteLine($"      Tentativa {tentativasOpcao}: {opcoesDropdown.Count} opções.");

                            foreach (var opt in opcoesDropdown)
                            {
                                try
                                {
                                    string textoOpt = opt.Text ?? "";
                                    if (textoOpt.Contains("100"))
                                    {
                                        Console.WriteLine($"      ✅ Opção '100' encontrada: '{textoOpt}'");

                                        jsExecutor.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", opt);
                                        Thread.Sleep(500);

                                        try
                                        {
                                            opt.Click();
                                            Console.WriteLine("      ✅ Opção '100' clicada.");
                                        }
                                        catch (ElementClickInterceptedException)
                                        {
                                            jsExecutor.ExecuteScript("arguments[0].click();", opt);
                                            Console.WriteLine("      ✅ Clique via JavaScript na opção.");
                                        }

                                        opcao100Selecionada = true;
                                        Thread.Sleep(3000);
                                        break;
                                    }
                                }
                                catch (StaleElementReferenceException)
                                {
                                    continue;
                                }
                            }

                            if (!opcao100Selecionada)
                            {
                                Console.WriteLine($"      ⚠️ Opção '100' não encontrada. Aguardando...");
                                Thread.Sleep(1500);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"      ⚠️ Erro: {ex.Message}");
                            Thread.Sleep(1000);
                        }
                    }

                    if (opcao100Selecionada)
                    {
                        Console.WriteLine("   ✅ Configuração para 100 itens concluída!");
                    }
                    else
                    {
                        Console.WriteLine("   ⚠️ Não foi possível selecionar opção '100'. Continuando com valor padrão.");
                    }
                }
                catch (StaleElementReferenceException)
                {
                    Console.WriteLine("   ❌ Dropdown ficou obsoleto. Continuando com valor padrão.");
                }
            }
            else
            {
                Console.WriteLine("   ⚠️ Dropdown de itens não encontrado. Continuando com valor padrão.");
            }

            // --- PASSO 5: COLETAR LINKS DOS EDITAIS ---
            Console.WriteLine("\n🔎 Coletando links dos editais encontrados...");
            
            List<string> todosLinksEditais = new List<string>();
            int paginaAtual = 1;
            bool temProximaPagina = true;
            
            while (temProximaPagina)
            {
                Console.WriteLine($"\n📄 Processando página {paginaAtual}...");
                Thread.Sleep(3000);
                
                var links = driver.FindElements(By.TagName("a")).ToList();
                
                var linksEditais = links.Where(l => 
                    l.Text != null && 
                    (l.Text.Contains("Aviso") || 
                     l.Text.Contains("Edital") || 
                     l.Text.Contains("Contratação") ||
                     (l.GetAttribute("href") != null && l.GetAttribute("href").Contains("/edital/")) ||
                     (l.GetAttribute("href") != null && l.GetAttribute("href").Contains("/contratacao/")))
                ).ToList();
                
                Console.WriteLine($"   Encontrados {linksEditais.Count} links de editais na página {paginaAtual}");
                
                foreach (var link in linksEditais)
                {
                    try
                    {
                        string href = link.GetAttribute("href");
                        if (!string.IsNullOrEmpty(href) && !todosLinksEditais.Contains(href))
                        {
                            todosLinksEditais.Add(href);
                            Console.WriteLine($"      📌 Link: {link.Text?.Trim()}");
                        }
                    }
                    catch { }
                }
                
                temProximaPagina = false;
                try
                {
                    var botoesProxima = driver.FindElements(By.XPath("//button[contains(text(), '›') or contains(text(), '»') or contains(@aria-label, 'próxima')]"));
                    
                    foreach (var btn in botoesProxima)
                    {
                        if (btn.Enabled && btn.Displayed)
                        {
                            Console.WriteLine($"   ➡️ Navegando para próxima página...");
                            jsExecutor.ExecuteScript("arguments[0].scrollIntoView(true);", btn);
                            Thread.Sleep(1000);
                            jsExecutor.ExecuteScript("arguments[0].click();", btn);
                            paginaAtual++;
                            temProximaPagina = true;
                            Thread.Sleep(5000);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Erro ao navegar: {ex.Message}");
                }
            }
            
            Console.WriteLine($"\n📊 TOTAL DE LINKS DE EDITAIS ENCONTRADOS: {todosLinksEditais.Count}");
            
            // ======================================================
            // PASSO 6: Processar CADA edital individualmente (CORRIGIDO)
            // ======================================================
            Console.WriteLine("\n🔍 Entrando em cada edital para extrair dados...");
            
            int editalAtual = 0;
            
            foreach (string urlEdital in todosLinksEditais)
            {
                editalAtual++;
                Console.WriteLine($"\n{new string('=', 60)}");
                Console.WriteLine($"📌 PROCESSANDO EDITAL {editalAtual}/{todosLinksEditais.Count}");
                Console.WriteLine($"🔗 URL: {urlEdital}");
                Console.WriteLine(new string('=', 60));
                
                try
                {
                    // Abrir em nova guia
                    jsExecutor.ExecuteScript("window.open(arguments[0], '_blank');", urlEdital);
                    Thread.Sleep(2000);
                    
                    driver.SwitchTo().Window(driver.WindowHandles[driver.WindowHandles.Count - 1]);
                    
                    // ======================================================
                    // CORREÇÃO: AGUARDAR OS DADOS CARREGAREM DE VERDADE
                    // ======================================================
                    Console.WriteLine($"⏳ Aguardando carregamento dos dados...");
                    
                    WebDriverWait waitPagina = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                    
                    // AGUARDA ATÉ QUE O CAMPO "Unidade compradora" APAREÇA (SINAL DE DADOS CARREGADOS)
                    waitPagina.Until(drv => drv.FindElements(By.XPath("//*[contains(text(), 'Unidade compradora')]")).Count > 0);
                    
                    Console.WriteLine($"✅ Dados carregados!");
                    Thread.Sleep(2000); // Aguarda mais 2 segundos para garantir
                    
                    var dados = new DadosEdital();
                    dados.UrlEdital = urlEdital;
                    
                    // --- FUNÇÃO AUXILIAR PARA EXTRAIR CAMPOS (CORRIGIDA) ---
                    string ExtrairCampo(string rotulo)
                    {
                        try
                        {
                            var elemento = driver.FindElement(By.XPath($"//*[contains(text(), '{rotulo}')]/following-sibling::*[1]"));
                            string valor = elemento.Text?.Trim() ?? "";
                            
                            if (valor.Contains("Fonte:"))
                            {
                                int idx = valor.IndexOf("Fonte:");
                                if (idx > 0)
                                {
                                    valor = valor.Substring(0, idx).Trim();
                                }
                            }
                            
                            return valor;
                        }
                        catch
                        {
                            return "";
                        }
                    }
                    
                    // --- EXTRAIR LOCAL ---
                    dados.Local = ExtrairCampo("Local:");
                    if (string.IsNullOrEmpty(dados.Local))
                    {
                        try
                        {
                            var localElement = driver.FindElement(By.XPath("//*[contains(text(), 'Local:')]/following-sibling::span"));
                            dados.Local = localElement.Text?.Trim() ?? "Não informado";
                        }
                        catch
                        {
                            dados.Local = "Não informado";
                        }
                    }
                    
                    // --- EXTRAIR UNIDADECOMPRADORA ---
                    Console.WriteLine($"🔍 Buscando Unidade Compradora...");
                    
                    string textoCompletoUnidade = "Não informado";
                    
                    // Estratégia 1: Buscar pelo padrão de UASG
                    try
                    {
                        var elementosComDigitos = driver.FindElements(By.XPath("//*[contains(text(), '393')]"))
                               .Where(el => Regex.IsMatch(el.Text ?? "", @"\b393\d{3}\b"))
                               .ToList();
                        
                        if (elementosComDigitos.Any())
                        {
                            var elemento = elementosComDigitos.First();
                            textoCompletoUnidade = elemento.Text?.Trim() ?? "";
                            Console.WriteLine($"   ✅ Unidade encontrada por padrão UASG");
                        }
                    }
                    catch { }
                    
                    // Estratégia 2: Buscar pelo rótulo
                    if (textoCompletoUnidade == "Não informado")
                    {
                        try
                        {
                            var rotulo = driver.FindElement(By.XPath("//*[contains(text(), 'Unidade compradora')]"));
                            var valor = rotulo.FindElement(By.XPath("./following-sibling::*[1]"));
                            textoCompletoUnidade = valor.Text?.Trim() ?? "Não informado";
                            Console.WriteLine($"   ✅ Unidade encontrada pelo rótulo");
                        }
                        catch { }
                    }
                    
                    dados.UnidadeCompradora = textoCompletoUnidade;
                    
                    // --- EXTRAIR ÓRGÃO ---
                    dados.Orgao = ExtrairCampo("Órgão:");
                    if (string.IsNullOrEmpty(dados.Orgao)) dados.Orgao = "Não informado";
                    
                    // --- EXTRAIR MODALIDADE ---
                    dados.Modalidade = ExtrairCampo("Modalidade da contratação:");
                    if (string.IsNullOrEmpty(dados.Modalidade)) dados.Modalidade = "Não informado";
                    
                    // --- EXTRAIR AMPARO LEGAL ---
                    dados.AmparoLegal = ExtrairCampo("Amparo legal:");
                    if (string.IsNullOrEmpty(dados.AmparoLegal)) dados.AmparoLegal = "Não informado";
                    
                    // --- EXTRAIR TIPO ---
                    dados.Tipo = ExtrairCampo("Tipo:");
                    if (string.IsNullOrEmpty(dados.Tipo)) dados.Tipo = "Não informado";
                    
                    // --- EXTRAIR MODO DE DISPUTA ---
                    dados.ModoDisputa = ExtrairCampo("Modo de disputa:");
                    if (string.IsNullOrEmpty(dados.ModoDisputa)) dados.ModoDisputa = "Não informado";
                    
                    // --- EXTRAIR REGISTRO DE PREÇO ---
                    dados.RegistroPreco = ExtrairCampo("Registro de preço:");
                    if (string.IsNullOrEmpty(dados.RegistroPreco)) dados.RegistroPreco = "Não informado";
                    
                    // --- EXTRAIR FONTE ORÇAMENTÁRIA ---
                    dados.FonteOrcamentaria = ExtrairCampo("Fonte orçamentária:");
                    if (string.IsNullOrEmpty(dados.FonteOrcamentaria)) dados.FonteOrcamentaria = "Não informado";
                    
                    // --- EXTRAIR DATA DE DIVULGAÇÃO ---
                    dados.DataDivulgacao = ExtrairCampo("Data de divulgação no PNCP:");
                    if (string.IsNullOrEmpty(dados.DataDivulgacao)) dados.DataDivulgacao = "Não informado";
                    
                    // --- EXTRAIR SITUAÇÃO ---
                    dados.Situacao = ExtrairCampo("Situação:");
                    if (string.IsNullOrEmpty(dados.Situacao)) dados.Situacao = "Não informado";
                    
                    // --- EXTRAIR INÍCIO PROPOSTAS ---
                    dados.InicioPropostas = ExtrairCampo("Data de início de recebimento de propostas:");
                    if (string.IsNullOrEmpty(dados.InicioPropostas)) dados.InicioPropostas = "Não informado";
                    
                    // --- EXTRAIR FIM PROPOSTAS ---
                    dados.FimPropostas = ExtrairCampo("Data fim de recebimento de propostas:");
                    if (string.IsNullOrEmpty(dados.FimPropostas)) dados.FimPropostas = "Não informado";
                    
                    // --- EXTRAIR ID PNCP ---
                    dados.IdPNCP = ExtrairCampo("Id contratação PNCP:");
                    if (string.IsNullOrEmpty(dados.IdPNCP)) dados.IdPNCP = "Não informado";
                    
                    // --- EXTRAIR OBJETO (CORRIGIDO) ---
                    try
                    {
                        var objElement = driver.FindElement(By.XPath("//h5[contains(text(), 'Objeto')]/following-sibling::p"));
                        dados.Objeto = objElement.Text?.Trim() ?? "Não informado";
                    }
                    catch
                    {
                        try
                        {
                            var objElement = driver.FindElement(By.XPath("//*[contains(text(), 'Objeto')]/following-sibling::*"));
                            dados.Objeto = objElement.Text?.Trim() ?? "Não informado";
                        }
                        catch
                        {
                            dados.Objeto = "Não informado";
                        }
                    }
                    
                    // --- EXTRAIR INFORMAÇÃO COMPLEMENTAR (CORRIGIDO) ---
                    try
                    {
                        var infoElement = driver.FindElement(By.XPath("//*[contains(text(), 'Informação complementar')]/following-sibling::p"));
                        dados.InformacaoComplementar = infoElement.Text?.Trim() ?? "Não informado";
                    }
                    catch
                    {
                        try
                        {
                            var infoElement = driver.FindElement(By.XPath("//*[contains(text(), 'Informação complementar')]/following-sibling::*"));
                            dados.InformacaoComplementar = infoElement.Text?.Trim() ?? "Não informado";
                        }
                        catch
                        {
                            dados.InformacaoComplementar = "Não informado";
                        }
                    }
                    
                    // --- EXTRAIR VALOR ESTIMADO (CORRIGIDO) ---
                    try
                    {
                        var valorElement = driver.FindElement(By.XPath("//*[contains(text(), 'VALOR TOTAL ESTIMADO')]/following-sibling::*[1]"));
                        dados.ValorEstimado = valorElement.Text?.Trim() ?? "Não informado";
                    }
                    catch
                    {
                        try
                        {
                            var valorElement = driver.FindElement(By.XPath("//*[contains(text(), 'Valor estimado')]/following-sibling::*[1]"));
                            dados.ValorEstimado = valorElement.Text?.Trim() ?? "Não informado";
                        }
                        catch
                        {
                            dados.ValorEstimado = "Não informado";
                        }
                    }
                    
                    // --- MOSTRAR RESUMO DOS DADOS EXTRAÍDOS ---
                    Console.WriteLine("\n📋 DADOS EXTRAÍDOS:");
                    Console.WriteLine($"   📍 Local: {dados.Local}");
                    Console.WriteLine($"   🏢 Unidade Compradora: {dados.UnidadeCompradora}");
                    Console.WriteLine($"   🏛️ Órgão: {dados.Orgao}");
                    Console.WriteLine($"   📊 Modalidade: {dados.Modalidade}");
                    Console.WriteLine($"   ⚖️ Amparo Legal: {dados.AmparoLegal}");
                    Console.WriteLine($"   📄 Tipo: {dados.Tipo}");
                    Console.WriteLine($"   🎯 Modo de Disputa: {dados.ModoDisputa}");
                    Console.WriteLine($"   💵 Registro de Preço: {dados.RegistroPreco}");
                    Console.WriteLine($"   💰 Fonte Orçamentária: {dados.FonteOrcamentaria}");
                    Console.WriteLine($"   📅 Data Divulgação: {dados.DataDivulgacao}");
                    Console.WriteLine($"   🔄 Situação: {dados.Situacao}");
                    Console.WriteLine($"   ⏱️ Início Propostas: {dados.InicioPropostas}");
                    Console.WriteLine($"   ⏱️ Fim Propostas: {dados.FimPropostas}");
                    Console.WriteLine($"   🔢 ID PNCP: {dados.IdPNCP}");
                    Console.WriteLine($"   📝 Objeto: {(dados.Objeto.Length > 80 ? dados.Objeto.Substring(0, 80) + "..." : dados.Objeto)}");
                    Console.WriteLine($"   ℹ️ Info Complementar: {(dados.InformacaoComplementar.Length > 50 ? dados.InformacaoComplementar.Substring(0, 50) + "..." : dados.InformacaoComplementar)}");
                    Console.WriteLine($"   💲 Valor Estimado: {dados.ValorEstimado}");
                    
                    // Adicionar ao CSV
                    csv.AppendLine($"{dados.Local};{dados.UnidadeCompradora};{dados.Orgao};{dados.Modalidade};{dados.AmparoLegal};{dados.Tipo};{dados.ModoDisputa};{dados.RegistroPreco};{dados.FonteOrcamentaria};{dados.DataDivulgacao};{dados.Situacao};{dados.InicioPropostas};{dados.FimPropostas};{dados.IdPNCP};\"{dados.Objeto.Replace("\"", "\"\"")}\";\"{dados.InformacaoComplementar.Replace("\"", "\"\"")}\";{dados.ValorEstimado};{dados.UrlEdital}");
                    
                    // Fechar guia e voltar para a lista
                    driver.Close();
                    driver.SwitchTo().Window(driver.WindowHandles[0]);
                    
                    if (editalAtual % 5 == 0)
                    {
                        File.WriteAllText(caminhoCsv, csv.ToString(), Encoding.UTF8);
                        Console.WriteLine($"\n💾 CHECKPOINT SALVO! {editalAtual} editais processados.");
                    }
                    
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro ao processar edital: {ex.Message}");
                    
                    try
                    {
                        if (driver.WindowHandles.Count > 1)
                        {
                            driver.Close();
                            driver.SwitchTo().Window(driver.WindowHandles[0]);
                        }
                    }
                    catch { }
                }
            }
            
            File.WriteAllText(caminhoCsv, csv.ToString(), Encoding.UTF8);
            
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("✅ EXTRAÇÃO CONCLUÍDA COM SUCESSO!");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"📊 Total de editais processados: {editalAtual}");
            Console.WriteLine($"📁 CSV gerado em: {caminhoCsv}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Erro geral: {ex.Message}");
            
            try
            {
                File.WriteAllText(caminhoCsv, csv.ToString(), Encoding.UTF8);
                Console.WriteLine($"💾 CSV parcial salvo em: {caminhoCsv}");
            }
            catch { }
        }
        finally
        {
            Console.WriteLine("\nPressione ENTER para fechar...");
            Console.ReadLine();
            driver.Quit();
        }
    }
}

class DadosEdital
{
    public string Local { get; set; } = "Não informado";
    public string UnidadeCompradora { get; set; } = "Não informado";
    public string Orgao { get; set; } = "Não informado";
    public string Modalidade { get; set; } = "Não informado";
    public string AmparoLegal { get; set; } = "Não informado";
    public string Tipo { get; set; } = "Não informado";
    public string ModoDisputa { get; set; } = "Não informado";
    public string RegistroPreco { get; set; } = "Não informado";
    public string FonteOrcamentaria { get; set; } = "Não informado";
    public string DataDivulgacao { get; set; } = "Não informado";
    public string Situacao { get; set; } = "Não informado";
    public string InicioPropostas { get; set; } = "Não informado";
    public string FimPropostas { get; set; } = "Não informado";
    public string IdPNCP { get; set; } = "Não informado";
    public string Objeto { get; set; } = "Não informado";
    public string InformacaoComplementar { get; set; } = "Não informado";
    public string ValorEstimado { get; set; } = "Não informado";
    public string UrlEdital { get; set; } = "Não informado";
}