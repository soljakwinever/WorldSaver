#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class EventDataTests
    {
        [Test]
        public void DisplayNameUsesAuthoredValue()
        {
            EventData data = ScriptableObject.CreateInstance<EventData>();
            try
            {
                data.name = "Asset Name";
                data.displayName = "  Display Name  ";

                Assert.That(data.DisplayName, Is.EqualTo("Display Name"));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void DisplayNameFallsBackToAssetName(string displayName)
        {
            EventData data = ScriptableObject.CreateInstance<EventData>();
            try
            {
                data.name = "Asset Name";
                data.displayName = displayName;

                Assert.That(data.DisplayName, Is.EqualTo("Asset Name"));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void DisabledSpawnRuleSuppressesItsNestedTraversal()
        {
            EnemySpawnRule parent =
                ScriptableObject.CreateInstance<EnemySpawnRule>();
            EnemySpawnRule child =
                ScriptableObject.CreateInstance<EnemySpawnRule>();
            try
            {
                parent.rules = new[] { child };
                IReadOnlyCollection<EnemySpawnRule> disabled =
                    new HashSet<EnemySpawnRule> { parent };

                Assert.That(Traverse(parent, disabled), Is.Empty);
                Assert.That(
                    Traverse(child, disabled),
                    Is.EqualTo(new[] { child }));
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(child);
            }
        }

        [TestCase(8, 17, 8, true)]
        [TestCase(8, 17, 16, true)]
        [TestCase(8, 17, 17, false)]
        [TestCase(22, 6, 23, true)]
        [TestCase(22, 6, 5, true)]
        [TestCase(22, 6, 12, false)]
        [TestCase(8, 8, 0, true)]
        [TestCase(8, 8, 23, true)]
        public void TimeOfDayConditionSupportsHourlyWindows(
            int firstHour,
            int lastHour,
            int currentHour,
            bool expected)
        {
            TimeOfDayEventCondition condition = new()
            {
                firstHour = firstHour,
                lastHour = lastHour
            };

            Assert.That(condition.AllowsHour(currentHour), Is.EqualTo(expected));
        }

        [Test]
        public void NestedTimeOfDayConditionUsesHourlyEvaluation()
        {
            EventCondition condition = new NotEventCondition
            {
                condition = new AllEventCondition
                {
                    conditions = new List<EventCondition>
                    {
                        new WeatherEventCondition(),
                        new TimeOfDayEventCondition()
                    }
                }
            };

            MethodInfo method = typeof(EventService).GetMethod(
                "ContainsTimeOfDayCondition",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { condition }), Is.True);
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(6, true)]
        public void EveryThreeDaysMatchesExpectedDayCadence(
            long absoluteDay,
            bool expected)
        {
            CalendarEventCondition calendar = new()
            {
                unit = EventTimeUnit.Days,
                every = 3,
                offset = 0
            };

            Assert.That(MatchesCalendar(calendar, absoluteDay), Is.EqualTo(expected));
        }

        [Test]
        public void CalendarOffsetShiftsRecurringSchedule()
        {
            CalendarEventCondition calendar = new()
            {
                unit = EventTimeUnit.Days,
                every = 3,
                offset = 1
            };

            Assert.That(MatchesCalendar(calendar, 0), Is.False);
            Assert.That(MatchesCalendar(calendar, 1), Is.True);
            Assert.That(MatchesCalendar(calendar, 4), Is.True);
        }

        [TestCase(17, false)]
        [TestCase(18, true)]
        [TestCase(19, false)]
        public void RecurringScheduleMatchesOnlyConfiguredHour(
            int currentHour,
            bool expected)
        {
            CalendarEventCondition calendar = new()
            {
                unit = EventTimeUnit.Days,
                every = 3
            };
            TimeOfDayEventCondition time = new()
            {
                firstHour = 18,
                lastHour = 19
            };

            bool matches = MatchesCalendar(calendar, 3) &&
                           time.AllowsHour(currentHour);
            Assert.That(matches, Is.EqualTo(expected));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void RecurringScheduleIsCheckedOnlyOnHourlyEvaluation(
            bool hourlyEvaluation,
            bool expected)
        {
            EventCondition schedule = new AllEventCondition
            {
                conditions = new List<EventCondition>
                {
                    new CalendarEventCondition(),
                    new TimeOfDayEventCondition
                    {
                        firstHour = 18,
                        lastHour = 19
                    }
                }
            };

            MethodInfo method = typeof(EventService).GetMethod(
                "ShouldEvaluate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.Invoke(null, new object[] { schedule, hourlyEvaluation }),
                Is.EqualTo(expected));
        }

        private static bool MatchesCalendar(
            CalendarEventCondition condition,
            long absoluteDay)
        {
            MethodInfo method = typeof(EventService).GetMethod(
                "MatchesCalendar",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(
                null,
                new object[] { condition, absoluteDay });
        }

        private static EnemySpawnRule[] Traverse(
            EnemySpawnRule root,
            IReadOnlyCollection<EnemySpawnRule> disabled)
        {
            MethodInfo method = typeof(NPCSpawnController).GetMethod(
                "Traverse",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            object result = method.Invoke(
                null,
                new object[]
                {
                    root,
                    new HashSet<EnemySpawnRule>(),
                    disabled
                });
            return ((IEnumerable<EnemySpawnRule>)result).ToArray();
        }
    }
}
#endif
