teste

Essas são as referências

	private Projeto _projetoOriginalNoBd;

	public ProjetoCapacitacaoBo(DbContext contexto, UnitOfWork<FunadespContext> uow = null)
		: base(contexto, uow)
	{
	}