using FreeSql.DataAnnotations;
using System;
using System.Collections.Generic;
using Xunit;

namespace FreeSql.Tests
{
    public class RepositoryTests
    {

        [Fact]
        public void AddUpdate()
        {
            var repos = g.sqlite.GetGuidRepository<AddUpdateInfo>();

            var item = repos.Insert(new AddUpdateInfo());
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));

            item = repos.Insert(new AddUpdateInfo { Id = Guid.NewGuid() });
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));

            item.Title = "xxx";
            repos.Update(item);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));

            Console.WriteLine(repos.UpdateDiy.Where(a => a.Id == item.Id).Set(a => a.Clicks + 1).ToSql());
            repos.UpdateDiy.Where(a => a.Id == item.Id).Set(a => a.Clicks + 1).ExecuteAffrows();

            item = repos.Find(item.Id);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));
        }

        [Fact]
        public void UpdateAttach()
        {
            var repos = g.sqlite.GetGuidRepository<AddUpdateInfo>();

            var item = new AddUpdateInfo { Id = Guid.NewGuid() };
            repos.Attach(item);

            item.Title = "xxx";
            repos.Update(item); //����ִ�� UPDATE "AddUpdateInfo" SET "Title" = 'xxx' WHERE("Id" = '1942fb53-9700-411d-8895-ce4cecdf3257')
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));

            repos.Update(item); //���в�ִ�� SQL��δ�仯

            repos.AttachOnlyPrimary(item).Update(item); //���и���״ֵ̬��ֻ������ֵ���ڣ�ִ�и��� set title = xxx

            Console.WriteLine(repos.UpdateDiy.Where(a => a.Id == item.Id).Set(a => a.Clicks + 1).ToSql());
            repos.UpdateDiy.Where(a => a.Id == item.Id).Set(a => a.Clicks + 1).ExecuteAffrows();

            item = repos.Find(item.Id);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));
        }

        [Fact]
        public void UpdateWhenNotExists()
        {
            var repos = g.sqlite.GetGuidRepository<AddUpdateInfo>();

            var item = new AddUpdateInfo { Id = Guid.NewGuid() };
            item.Title = "xxx";
            Assert.Throws<Exception>(() => repos.Update(item));
        }

        [Fact]
        public void Update()
        {
            g.sqlite.Insert(new AddUpdateInfo()).ExecuteAffrows();

            var repos = g.sqlite.GetGuidRepository<AddUpdateInfo>();

            var item = new AddUpdateInfo { Id = g.sqlite.Select<AddUpdateInfo>().First().Id };

            item.Title = "xxx";
            repos.Update(item);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(item));
        }

        public class AddUpdateInfo
        {

            public Guid Id { get; set; }
            public string Title { get; set; }

            public int Clicks { get; set; } = 10;
        }

        [Fact]
        public void UnitOfWorkRepository()
        {
            foreach (var fsql in new[] { g.sqlite, /*g.mysql, g.pgsql, g.oracle, g.sqlserver*/ })
            {

                fsql.CodeFirst.ConfigEntity<FlowModel>(f =>
                {
                    f.Property(b => b.UserId).IsPrimary(true);
                    f.Property(b => b.Id).IsPrimary(true).IsIdentity(true);
                    f.Property(b => b.Name).IsNullable(false);
                });

                FlowModel flow = new FlowModel()
                {
                    CreateTime = DateTime.Now,
                    Name = "aaa",
                    LastModifyTime = DateTime.Now,
                    UserId = 1,
                };
                var flowRepos = fsql.GetRepository<FlowModel>();
                flowRepos.Insert(flow);

                //�������
                flow = new FlowModel()
                {
                    CreateTime = DateTime.Now,
                    Name = "aaa",
                    LastModifyTime = DateTime.Now,
                    UserId = 1,
                };
                using (var uow = fsql.CreateUnitOfWork())
                {
                    flowRepos = uow.GetRepository<FlowModel>();
                    flowRepos.Insert(flow);
                    uow.Commit();
                }
            }
        }

        [Fact]
        public void UnitOfWorkRepositoryWithDisableBeforeInsert()
        {
            foreach (var fsql in new[] { g.sqlite, })
            {
                fsql.CodeFirst.ConfigEntity<FlowModel>(f =>
                {
                    f.Property(b => b.UserId).IsPrimary(true);
                    f.Property(b => b.Id).IsPrimary(true).IsIdentity(true);
                    f.Property(b => b.Name).IsNullable(false);
                });

                var flowRepos = fsql.GetRepository<FlowModel>();

                var flow = new FlowModel()
                {
                    CreateTime = DateTime.Now,
                    Name = "aaa",
                    LastModifyTime = DateTime.Now,
                    UserId = 1,
                };

                //��������ݿ����Ѵ��ڵ����ݣ�Ϊ�˽������Ĳ������
                flowRepos.Delete(a => a.UserId == 1 && a.Name == "aaa");

                using (var uow = fsql.CreateUnitOfWork())
                {
                    //�رչ�����Ԫ�����Ὺʼ����
                    uow.Close();
                    var uowFlowRepos = uow.GetRepository<FlowModel>();
                    uowFlowRepos.Insert(flow);
                    //�ѹرչ�����Ԫ���᲻�ύ��ûӰ�죬�˴�ע����ȷ��������Ԫ�����Ƿ���Ч���ر��ˣ���CommitҲӦ�ò�������
                    //uow.Commit();
                }

                Assert.True(flowRepos.Select.Any(a => a.UserId == 1 && a.Name == "aaa"));
            }

        }

        [Fact]
        public void UnitOfWorkRepositoryWithDisableAfterInsert()
        {
            foreach (var fsql in new[] { g.sqlite, })
            {
                fsql.CodeFirst.ConfigEntity<FlowModel>(f =>
                {
                    f.Property(b => b.UserId).IsPrimary(true);
                    f.Property(b => b.Id).IsPrimary(true).IsIdentity(true);
                    f.Property(b => b.Name).IsNullable(false);
                });

                var flowRepos = fsql.GetRepository<FlowModel>();

                //��������ݿ����Ѵ��ڵ����ݣ�Ϊ�˽������Ĳ������
                flowRepos.Delete(a => a.UserId == 1 && a.Name == "aaa");

                var flow = new FlowModel()
                {
                    CreateTime = DateTime.Now,
                    Name = "aaa",
                    LastModifyTime = DateTime.Now,
                    UserId = 1,
                };


                Assert.Throws<Exception>(() =>
                {
                    using (var uow = fsql.CreateUnitOfWork())
                    {
                        var uowFlowRepos = uow.GetRepository<FlowModel>();
                        uowFlowRepos.Insert(flow);
                        //�������� Insert/Update/Delete ���ùر�uow�ķ������ᷢ���쳣
                        uow.Close();
                        uow.Commit();
                    }

                });
            }
        }

        [Fact]
        public void UnitOfWorkRepositoryWithoutDisable()
        {
            foreach (var fsql in new[] { g.sqlite, })
            {
                fsql.CodeFirst.ConfigEntity<FlowModel>(f =>
                {
                    f.Property(b => b.UserId).IsPrimary(true);
                    f.Property(b => b.Id).IsPrimary(true).IsIdentity(true);
                    f.Property(b => b.Name).IsNullable(false);
                });

                var flowRepos = fsql.GetRepository<FlowModel>();
                if (flowRepos.Select.Any(a => a.UserId == 1 && a.Name == "aaa"))
                {
                    flowRepos.Delete(a => a.UserId == 1);
                }


                var flow = new FlowModel()
                {
                    CreateTime = DateTime.Now,
                    Name = "aaa",
                    LastModifyTime = DateTime.Now,
                    UserId = 1,
                };


                using (var uow = fsql.CreateUnitOfWork())
                {
                    var uowFlowRepos = uow.GetRepository<FlowModel>();
                    uowFlowRepos.Insert(flow);
                    //������commit�������ύ���ݿ����
                    //uow.Commit();
                }
                Assert.False(flowRepos.Select.Any(a => a.UserId == 1 && a.Name == "aaa"));
            }
        }


        public partial class FlowModel
        {
            public int UserId { get; set; }
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public string Name { get; set; }
            public DateTime CreateTime { get; set; }
            public DateTime LastModifyTime { get; set; }
            public string Desc { get; set; }
        }

        [Fact]
        public void AsType()
        {
            g.sqlite.Insert(new AddUpdateInfo()).ExecuteAffrows();

            var repos = g.sqlite.GetGuidRepository<object>();
            repos.AsType(typeof(AddUpdateInfo));

            var item = new AddUpdateInfo();
            repos.Insert(item);
            repos.Update(item);

            item.Clicks += 1;
            repos.InsertOrUpdate(item);

            var item2 = repos.Find(item.Id) as AddUpdateInfo;
            Assert.Equal(item.Clicks, item2.Clicks);

            repos.DataFilter.Apply("xxx", a => (a as AddUpdateInfo).Clicks == 2);
            Assert.Null(repos.Find(item.Id));
        }

        [Fact]
        public void EnableAddOrUpdateNavigateList_OneToMany()
        {
            var repo = g.sqlite.GetRepository<Cagetory>();
            var cts = new[] {
                new Cagetory
                {
                    Name = "����1",
                    Goodss = new List<Goods>(new[]
                    {
                        new Goods { Name = "��Ʒ1" },
                        new Goods { Name = "��Ʒ2" },
                        new Goods { Name = "��Ʒ3" }
                    })
                },
                new Cagetory
                {
                    Name = "����2",
                    Goodss = new List<Goods>(new[]
                    {
                        new Goods { Name = "��Ʒ4" },
                        new Goods { Name = "��Ʒ5" }
                    })
                }
            };
            repo.Insert(cts);
            cts[0].Name = "����11";
            cts[0].Goodss.Clear();
            cts[1].Name = "����22";
            cts[1].Goodss.Clear();
            repo.Update(cts);
            cts[0].Name = "����111";
            cts[0].Goodss.Clear();
            cts[0].Goodss.Add(new Goods { Name = "��Ʒ33" });
            cts[1].Name = "����222";
            cts[1].Goodss.Clear();
            cts[1].Goodss.Add(new Goods { Name = "��Ʒ55" });
            repo.Update(cts);
        }
        [Table(Name = "EAUNL_OTM_CT")]
        class Cagetory
        {
            public Guid Id { get; set; }
            public string Name { get; set; }

            [Navigate("CagetoryId")]
            public List<Goods> Goodss { get; set; }
        }
        [Table(Name = "EAUNL_OTM_GD")]
        class Goods
        {
            public Guid Id { get; set; }
            public Guid CagetoryId { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void EnableAddOrUpdateNavigateList_OneToMany_Parent()
        {
            var repo = g.sqlite.GetRepository<CagetoryParent>();
            var cts = new[] {
                new CagetoryParent
                {
                    Name = "����1",
                    Childs = new List<CagetoryParent>(new[]
                    {
                        new CagetoryParent { Name = "����1_1" },
                        new CagetoryParent { Name = "����1_2" },
                        new CagetoryParent { Name = "����1_3" }
                    })
                },
                new CagetoryParent
                {
                    Name = "����2",
                    Childs = new List<CagetoryParent>(new[]
                    {
                        new CagetoryParent { Name = "����2_1" },
                        new CagetoryParent { Name = "����2_2" }
                    })
                }
            };
            repo.DbContextOptions.EnableAddOrUpdateNavigateList = false; //�رռ������湦��
            repo.Insert(cts);
            repo.SaveMany(cts[0], "Childs"); //ָ������ Childs һ�Զ�����
            cts[0].Name = "����11";
            cts[0].Childs.Clear();
            cts[1].Name = "����22";
            cts[1].Childs.Clear();
            repo.Update(cts);
            cts[0].Name = "����111";
            cts[0].Childs.Clear();
            cts[0].Childs.Add(new CagetoryParent { Name = "����1_33" });
            cts[1].Name = "����222";
            cts[1].Childs.Clear();
            cts[1].Childs.Add(new CagetoryParent { Name = "����2_22" });
            repo.Update(cts);
        }
        [Table(Name = "EAUNL_OTMP_CT")]
        class CagetoryParent
        {
            public Guid Id { get; set; }
            public string Name { get; set; }

            public Guid ParentId { get; set; }
            [Navigate("ParentId")]
            public List<CagetoryParent> Childs { get; set; }
        }

        [Fact]
        public void EnableAddOrUpdateNavigateList_ManyToMany()
        {
            var tags = new[] {
                new Tag { TagName = "����" },
                new Tag { TagName = "80��" },
                new Tag { TagName = "00��" },
                new Tag { TagName = "ҡ��" }
            };
            var ss = new[]
            {
                new Song
                {
                    Name = "����һ����.mp3",
                    Tags = new List<Tag>(new[]
                    {
                        tags[0], tags[1]
                    })
                },
                new Song
                {
                    Name = "���.mp3",
                    Tags = new List<Tag>(new[]
                    {
                        tags[0], tags[2]
                    })
                }
            };
            var repo = g.sqlite.GetRepository<Song>();
            //repo.DbContextOptions.EnableAddOrUpdateNavigateList = false; //�رռ������湦��
            repo.Insert(ss);
            //repo.SaveMany(ss[0], "Tags"); //ָ������ Tags ��Զ�����

            ss[0].Name = "����һ����.mp5";
            ss[0].Tags.Clear();
            ss[0].Tags.Add(tags[0]);
            ss[1].Name = "���.mp5";
            ss[1].Tags.Clear();
            ss[1].Tags.Add(tags[3]);
            repo.Update(ss);

            ss[0].Name = "����һ����.mp4";
            ss[0].Tags.Clear();
            ss[1].Name = "���.mp4";
            ss[1].Tags.Clear();
            repo.Update(ss);
        }
        [Table(Name = "EAUNL_MTM_SONG")]
        class Song
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public List<Tag> Tags { get; set; }
        }
        [Table(Name = "EAUNL_MTM_TAG")]
        class Tag
        {
            public Guid Id { get; set; }
            public string TagName { get; set; }
            public List<Song> Songs { get; set; }
        }
        [Table(Name = "EAUNL_MTM_SONGTAG")]
        class SongTag
        {
            public Guid SongId { get; set; }
            public Song Song { get; set; }
            public Guid TagId { get; set; }
            public Tag Tag { get; set; }
        }
    }
}
