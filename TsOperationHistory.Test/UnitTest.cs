﻿using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TsOperationHistory.Extensions;
using Xunit;

namespace TsOperationHistory.Test
{
    internal class Person : Bindable
    {
        private string _name;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private int _age;

        public int Age
        {
            get => _age;
            set => SetProperty(ref _age, value);
        }

        private ObservableCollection<Person> _children = new ObservableCollection<Person>();

        public ObservableCollection<Person> Children
        {
            get => _children;
            set => SetProperty(ref _children, value);
        }
    }
    
    public class UnitTest
    {
        /// <summary>
        /// 基本的なUndoRedoのテスト
        /// </summary>
        [Fact]
        public void BasicTest()
        {
            IOperationController controller = new OperationController();
            var person = new Person()
            {
                Name = "Venus",
            };

            controller.Execute(person.GenerateSetPropertyOperation(x=>x.Name , "Yamada"));
            Assert.Equal("Yamada",person.Name);

            controller.Execute(person.GenerateSetPropertyOperation(x=>x.Name , "Tanaka"));
            Assert.Equal("Tanaka",person.Name);
            
            controller.Undo();
            Assert.Equal("Yamada",person.Name);

            controller.Undo();
            Assert.Equal("Venus",person.Name);
        }
        
        /// <summary>
        /// Operationの自動結合テスト
        /// </summary>
        [Fact]
        public async void MergedTest()
        {
            IOperationController controller = new OperationController();

            var person = new Person()
            {
                Age = 14,
            };

            // デフォルトのマージ時間を 70msに設定
            Operation.DefaultMergeSpan = TimeSpan.FromMilliseconds(70);

            //Age = 30
            controller.ExecuteSetProperty(person,nameof(Person.Age),30);
            Assert.Equal(30, person.Age );
            
            //10 ms待つ
            await Task.Delay(10);
            
            //Age = 100
            controller.ExecuteSetProperty(person,nameof(Person.Age),100);
            Assert.Equal(100, person.Age );

            //100ms 待つ
            await Task.Delay(75);
            
            //Age = 150
            controller.ExecuteSetProperty(person,nameof(Person.Age),150);
            Assert.Equal(150, person.Age );
            
            //Age = 100
            controller.Undo();
            Assert.Equal(100, person.Age );

            // マージされているので 30には戻らずそのまま14に戻る
            // Age = 14
            controller.Undo();
            Assert.Equal(14, person.Age );
        }

        /// <summary>
        /// リスト操作のテスト
        /// </summary>
        [Fact]
        public void ListTest()
        {
            IOperationController controller = new OperationController();

            var person = new Person()
            {
               Name = "Root"
            };
            
            controller.ExecuteAdd(person.Children , 
                new Person()
                {
                    Name = "Child1"
                });
            
            controller.ExecuteAdd(person.Children , 
                new Person()
                {
                    Name = "Child2"
                });
            
            Assert.Equal(2 , person.Children.Count);
            
            controller.ExecuteRemoveAt(person.Children,0);
            Assert.Single(person.Children);
            
            controller.Undo();
            Assert.Equal(2 , person.Children.Count);
            
            controller.Undo();
            Assert.Single(person.Children);
            
            controller.Undo();
            Assert.Empty(person.Children);
        }

        /// <summary>
        /// PropertyChangedを自動的にOperation化するテスト
        /// </summary>
        [Fact]
        public void ObservePropertyChangedTest()
        {
            IOperationController controller = new OperationController();
            
            var person = new Person()
            {
                Name = "First",
                Age = 0,
            };

            var nameChangedWatcher = controller.BindPropertyChanged<string>(person, nameof(Person.Name),false);
            var ageChangedWatcher = controller.BindPropertyChanged<int>(person, nameof(Person.Age));

            // 変更通知から自動的に Undo / Redo が可能なOperationをスタックに積む
            {
                person.Name = "Yammada";
                person.Name = "Tanaka";
            
                Assert.True(controller.CanUndo);
            
                controller.Undo();
                Assert.Equal("Yammada",person.Name);

                controller.Undo();
                Assert.Equal("First",person.Name);                
            }

            // Dispose後は変更通知が自動的にOperationに変更されないことを確認
            {
                nameChangedWatcher.Dispose();
                person.Name = "Tanaka";
                Assert.False(controller.CanUndo);

                controller.Undo();
                Assert.Equal("Tanaka",person.Name);
            }

            // Ageは自動マージ有効なため1回のUndoで初期値に戻ることを確認
            {
                for (int i = 1; i < 30; ++i)
                {
                    person.Age = i;
                }
                
                Assert.Equal(29,person.Age);
                
                controller.Undo();
                Assert.Equal(0,person.Age);
                
                ageChangedWatcher.Dispose();
            }
        }
        
        
        [Fact]
        public void RecorderTest()
        {
            IOperationController controller = new OperationController();
            
            var person = new Person()
            {
                Name = "Default",
                Age = 5,
            };
            
            var recorder = new OperationRecorder(controller);
            
            // 操作の記録開始
            recorder.BeginRecode();
            {
                recorder.Current.ExecuteAdd(person.Children,new Person()
                {
                    Name = "Child1",
                });
            
                recorder.Current.ExecuteSetProperty(person , nameof(Person.Age) , 14);
            
                recorder.Current.ExecuteSetProperty(person , nameof(Person.Name) , "Changed");
            }
            // 操作の記録完了
            recorder.EndRecode("Fixed");
            
            // 1回のUndoでレコード前のデータが復元される
            controller.Undo();
            Assert.Equal("Default",person.Name);
            Assert.Equal(5,person.Age);
            Assert.Empty(person.Children);
            
            // Redoでレコード終了後のデータが復元される
            controller.Redo();
            Assert.Equal("Changed",person.Name);
            Assert.Equal(14,person.Age);
            Assert.Single(person.Children);
        }
    }
}