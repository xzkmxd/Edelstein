using System;
using System.Collections.Generic;
using System.Linq;

namespace Edelstein.Provider.Templates.String
{
    public class QuestStringTemplateCollection : AbstractEagerTemplateCollection
    {
        public override TemplateCollectionType Type => TemplateCollectionType.QuestString;

        public QuestStringTemplateCollection(IDataDirectoryCollection collection) : base(collection)
        {
        }

        protected override IEnumerable<ITemplate> Load()
        {
            var property = Collection.Resolve("Quest/QuestInfo.img");

            return property.Children
                .Select(c => new SkillStringTemplate(Convert.ToInt32(c.Name), c.ResolveAll()));
        }
    }
}