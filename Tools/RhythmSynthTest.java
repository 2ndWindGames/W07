import com.secondwindgames.runbeat.RhythmSynth;
import java.io.*;
import java.util.*;

public class RhythmSynthTest {
    static int checks;
    static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);checks++;}
    public static void main(String[] args)throws Exception {
        final int rate=48000;
        short[] buffer=new short[2048];
        for(int target:new int[]{100,160,170,180,190,220})for(int steps=1;steps<=2;steps++){
            RhythmSynth synth=new RhythmSynth(rate,target,steps,0,.65f);
            long[] count={0},previous={-1};double interval=RhythmSynth.intervalFrames(rate,target,steps);
            long total=(long)rate*1800;
            while(synth.frames()<total){
                int n=(int)Math.min(buffer.length,total-synth.frames());
                synth.render(buffer,n,(frame,applied)->{
                    check(Math.abs(frame-count[0]*interval)<=.501,"Sample clock precision "+target+"/"+applied);
                    check(frame>previous[0],"Monotonic beat deadline");previous[0]=frame;count[0]++;
                });
            }
            long expected=(long)Math.ceil(target*30.0/steps);
            check(count[0]==expected,"30 minute beat count");
            System.out.println("PASS 30 min "+target+" SPM / "+steps+" steps = "+count[0]+" beats, error <= 0.5 sample");
        }
        RhythmSynth changing=new RhythmSynth(rate,180,1,1,.8f);List<Integer> applied=new ArrayList<>();
        changing.render(buffer,2048,(f,t)->applied.add(t));changing.setTarget(200);
        for(int i=0;i<10;i++)changing.render(buffer,2048,(f,t)->applied.add(t));
        check(applied.get(0)==180 && applied.get(1)==200,"Target changes at next beat");
        for(int sound=0;sound<3;sound++){
            RhythmSynth tone=new RhythmSynth(rate,180,1,sound,1);short[] data=new short[2400];tone.render(data,data.length,null);
            int peak=0;for(short value:data)peak=Math.max(peak,Math.abs((int)value));
            check(peak>1000&&peak<32767,"Audible unclipped original tone "+sound);
        }
        System.out.println("TOTAL "+checks+" assertions passed");
    }
}
