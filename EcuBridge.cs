using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace ClaudeSidecar
{
    public sealed class ChannelInfo
    {
        public int Id;
        public string Name;
        public bool Exists;
        internal object Channel;
        internal EventHandler Handler;
    }

    /// <summary>
    /// Read-only access to ECU Manager's live sensor channels.
    /// Names come from ECU Manager 1.14.0: MainFrame.m_inputChannels (InputChannels), and
    /// InputChannel.get_Value / get_Exists / get_ChannelID / add_ValueChanged.
    /// Subscribing to ValueChanged makes ECU Manager add that channel to its ECU request loop.
    /// Nothing here writes to the ECU or to the map.
    /// </summary>
    public sealed class EcuBridge
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        readonly Form main;
        readonly FieldInfo fInputs;
        readonly MethodInfo mValue, mExists, mChannelId, mAddChanged, mRemoveChanged, mOnline;

        public EcuBridge(Assembly ecu, Form main)
        {
            this.main = main;
            fInputs = Require(main.GetType().GetField("m_inputChannels", All), "MainFrame.m_inputChannels");
            Type tChannel = Require(ecu.GetType("com.Haltech.ECUManager.Core.InputChannel", false), "InputChannel");
            Type tChannels = Require(ecu.GetType("com.Haltech.ECUManager.Core.InputChannels", false), "InputChannels");
            mValue = Require(tChannel.GetMethod("get_Value", All, null, Type.EmptyTypes, null), "InputChannel.Value");
            mExists = Require(tChannel.GetMethod("get_Exists", All, null, Type.EmptyTypes, null), "InputChannel.Exists");
            mChannelId = Require(tChannel.GetMethod("get_ChannelID", All, null, Type.EmptyTypes, null), "InputChannel.ChannelID");
            mAddChanged = Require(tChannel.GetMethod("add_ValueChanged", All), "InputChannel.ValueChanged");
            mRemoveChanged = Require(tChannel.GetMethod("remove_ValueChanged", All), "InputChannel.ValueChanged");
            mOnline = tChannels.GetMethod("get_Online", All, null, Type.EmptyTypes, null);
        }

        static T Require<T>(T member, string what) where T : class
        {
            if (member == null) throw new MissingMemberException("ECU Manager internals changed: couldn't find " + what + ". This sidecar was built for ECU Manager 1.14.0.");
            return member;
        }

        object Inputs { get { return fInputs.GetValue(main); } }

        public bool Online
        {
            get
            {
                try { object i = Inputs; return i != null && mOnline != null && (bool)mOnline.Invoke(i, null); }
                catch { return false; }
            }
        }

        /// <summary>Every channel ECU Manager knows about for the open map.</summary>
        public List<ChannelInfo> GetChannels()
        {
            var list = new List<ChannelInfo>();
            var inputs = Inputs as IEnumerable;
            if (inputs == null) return list;
            try
            {
                foreach (object ch in inputs)
                {
                    object id = mChannelId.Invoke(ch, null);
                    list.Add(new ChannelInfo
                    {
                        Id = Convert.ToInt32(id),
                        Name = id.ToString(),
                        Exists = (bool)mExists.Invoke(ch, null),
                        Channel = ch
                    });
                }
            }
            catch (InvalidOperationException) { /* collection changed while reading; caller retries */ }
            return list;
        }

        public int ReadRaw(ChannelInfo c) { return (int)mValue.Invoke(c.Channel, null); }

        /// <summary>Start receiving live values. Called on ECU Manager's comms thread, so keep onValue fast.</summary>
        public void Subscribe(ChannelInfo c, Action<ChannelInfo, int> onValue)
        {
            if (c.Handler != null) return;
            c.Handler = (s, e) =>
            {
                try { onValue(c, (int)mValue.Invoke(c.Channel, null)); } catch { }
            };
            mAddChanged.Invoke(c.Channel, new object[] { c.Handler });
        }

        public void Unsubscribe(ChannelInfo c)
        {
            if (c.Handler == null) return;
            try { mRemoveChanged.Invoke(c.Channel, new object[] { c.Handler }); } catch { }
            c.Handler = null;
        }
    }
}
