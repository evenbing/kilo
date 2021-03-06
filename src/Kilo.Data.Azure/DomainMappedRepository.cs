﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.WindowsAzure.Storage.Table;

namespace Kilo.Data.Azure
{
    public class DomainMappedRepository<TTable, TDomain> : IDuplexRepository<TTable, TDomain>
        where TTable : TableEntity, new()
        where TDomain : class
    {
        private UnitOfWorkContainer<TDomain> _uow = new UnitOfWorkContainer<TDomain>();
        private TableStorageRepository<TTable> _repository;

        /// <summary>
        /// Initializes a new instance of the <see cref="TableStorageRepository{TSource}" /> class.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        public DomainMappedRepository(StorageContext context, string tableName, IEntityMapper<TTable, TDomain> entityMapper = null)
        {
            this._repository = new TableStorageRepository<TTable>(context, tableName);

            this._repository.EntityInserting += this.OnBeforeInsert;
            this._repository.EntityUpdating += this.OnBeforeUpdate;
            this._repository.EntityDeleting += this.OnBeforeDelete;
            this._repository.BatchCommitted += this.OnBatchExecuted;

            this.ResetUnitOfWork();

            this.Mapper = entityMapper;
        }

        public IEntityMapper<TTable, TDomain> Mapper { get; private set; }

        /// <summary>
        /// Inserts the specified entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        public virtual void Insert(TDomain entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            this._uow.Insert(entity);
        }

        /// <summary>
        /// Updates the specified entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        public virtual void Update(TDomain entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            this._uow.Update(entity);
        }

        /// <summary>
        /// Deletes the specified entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        public virtual void Delete(TDomain entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            this._uow.Delete(entity);
        }

        /// <summary>
        /// Commits the operations which are currently in the unit of work
        /// </summary>
        public virtual void Commit()
        {
            this._uow.GetJournal().ForEach(j =>
            {
                switch(j.Type)
                {
                    case UnitOfWorkEntryType.Insert:
                        {
                            var tableEntity = this.ConvertToTableEntity(j.Entity);
                            this._repository.Insert(tableEntity);
                        }
                        break;

                    case UnitOfWorkEntryType.Update:
                        {
                            var tableEntity = this.ConvertToTableEntity(j.Entity);
                            this._repository.Update(tableEntity);
                        }
                        break;

                    case UnitOfWorkEntryType.Delete:
                        {
                            var tableEntity = this.ConvertToTableEntity(j.Entity);
                            this._repository.Delete(tableEntity);
                        }
                        break;
                }
            });

            this._repository.Commit();
            this.ResetUnitOfWork();
        }

        /// <summary>
        /// Gets entities based on a predicate filter.
        /// </summary>
        /// <param name="partitionKey">The partition key to filter on. A filter for partitionKey is automatically included by default.</param>
        /// <param name="predicates">The predicates to include in the filter.</param>
        public IQueryable<TDomain> Query(params Expression<Func<TTable, bool>>[] predicates)
        {
            var entities = this._repository.Query(predicates);

            return this.MapQuery(entities);
        }

        /// <summary>
        /// Gets entities based on a predicate filter.
        /// </summary>
        /// <param name="partitionKey">The partition key to filter on. A filter for partitionKey is automatically included by default.</param>
        /// <param name="predicates">The predicates to include in the filter.</param>
        public IQueryable<TDomain> QueryWithResolver(EntityResolver<TTable> resolver, params Expression<Func<TTable, bool>>[] predicates)
        {
            var entities = this._repository.QueryWithResolver(resolver, predicates);

            return this.MapQuery(entities);
        }

        /// <summary>
        /// Performs a raw table query, returning a query expression.
        /// </summary>
        /// <param name="predicates">The optional predicates to apply to the query</param>
        /// <returns>A query expression for retrieving the required data.</returns>
        public IQueryable<TTable> TableQuery(params Expression<Func<TTable, bool>>[] predicates)
        {
            return this._repository.Query(predicates);
        }

        /// <summary>
        /// Gets the entity.
        /// </summary>
        /// <param name="specifications">The specifications.</param>
        public TDomain Single(dynamic key)
        {
            var entity = this._repository.Single(key);

            if(entity != null)
            {
                return this.ConvertFromTableEntity(entity);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the first entity in the query based on the predicates.
        /// </summary>
        /// <param name="predicates">The predicates.</param>
        public TDomain First(params Expression<Func<TTable, bool>>[] predicates)
        {
            var entity = this._repository.First(predicates);

            if (entity != null)
            {
                return this.ConvertFromTableEntity(entity);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Called just before the entity is inserted into the table operation
        /// </summary>
        /// <param name="domainEntity">The source entity.</param>
        /// <param name="tableEntity">The table entity.</param>
        protected virtual void OnBeforeInsert(CommitContext<TTable> context)
        {
        }

        /// <summary>
        /// Called just before the entity is updated on the table.
        /// </summary>
        /// <param name="domainEntity">The source entity.</param>
        /// <param name="tableEntity">The table entity.</param>
        protected virtual void OnBeforeUpdate(CommitContext<TTable> context)
        {
        }

        /// <summary>
        /// Called just before an entity is deleted.
        /// </summary>
        /// <param name="context">The context.</param>
        protected virtual void OnBeforeDelete(CommitContext<TTable> context)
        {
        }

        /// <summary>
        /// Called just after a batch has been executed.
        /// </summary>
        protected virtual void OnBatchExecuted(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Converts the domain entity into an entity fit for table storage.
        /// </summary>
        /// <param name="entity">The entity.</param>
        public TTable ConvertToTableEntity(TDomain entity)
        {
            return this.Mapper.MapToEntity(entity);
        }

        /// <summary>
        /// Converts the table storage entity into a domain entity.
        /// </summary>
        /// <param name="entity"></param>
        public TDomain ConvertFromTableEntity(TTable entity)
        {
            return this.Mapper.MapFromEntity(entity);
        }

        /// <summary>
        /// Maps a table query into a domain queryable object. Note, this will execute the query in the process.
        /// </summary>
        /// <param name="query">The query to map</param>
        public IQueryable<TDomain> MapQuery(IQueryable<TTable> query)
        {
            var domainEntities = query.ToList()
                .Select(e => this.ConvertFromTableEntity(e));

            return domainEntities.AsQueryable();
        }

        /// <summary>
        /// Resets the unit of work.
        /// </summary>
        protected virtual void ResetUnitOfWork()
        {
            this._uow.Reset();
        }

        /// <summary>
        /// Rolls back the current pending changes.
        /// </summary>
        public void Rollback()
        {
            this.ResetUnitOfWork();
        }

        public void Attach(TDomain entity, State state = State.Unchanged)
        {
        }

        public void Detach(TDomain entity)
        {
        }
    }
}
