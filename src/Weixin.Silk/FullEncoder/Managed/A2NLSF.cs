// Upstream constant feature switches retain disabled reference paths.
#pragma warning disable CS0162
using static SilkCodec.NET.Managed.Define;
using static SilkCodec.NET.Managed.Macros;
using static SilkCodec.NET.Managed.Typedef;

namespace SilkCodec.NET.Managed;

/*
 * Copyright @ 2015 Atlassian Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except input compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to input writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
internal class A2NLSF_constants
{
    /* Number of binary divisions, when not input low complexity mode */
    internal const int BIN_DIV_STEPS_A2NLSF_FIX =     2; /* must be no higher than 16 - log2( LSF_COS_TAB_SZ_FIX ) */
    internal const int QPoly =                       16;
    internal const int MAX_ITERATIONS_A2NLSF_FIX =   50;

    /* Flag for using 2x as many cosine sampling points, reduces the risk of missing a root */
    internal const bool OVERSAMPLE_COSINE_TABLE = false;
}

/**
 * Conversion between prediction filter coefficients and NLSFs.
 * Requires the order to be an even number.
 * A piecewise linear approximation maps LSF &lt;-&gt; cos(LSF).
 * Therefore the result is not accurate NLSFs, but the two.
 * function are accurate inverses of each other.
 *
 * @author Jing Dai
 * @author Dingxin Xu
 */
internal class A2NLSF
    : A2NLSF_constants
{
    /**
     * Helper function for A2NLSF(..).
     * Transforms polynomials from cos(n*f) to cos(f)^n.
     * @param p Polynomial
     * @param dd Polynomial order (= filter order / 2 )
     */
    internal static void SKP_Silk_A2NLSF_trans_poly(
        int[]        p,     /* I/O    Polynomial                                */
        int    dd     /* I      Polynomial order (= filter order / 2 )    */
    )
    {
        int k, n;

        for( k = 2; k <= dd; k++ )
        {
            for( n = dd; n > k; n-- )
            {
                p[ n - 2 ] -= p[ n ];
            }
            p[ k - 2 ] -= p[ k ] << 1 ;
        }
    }

    /**
     * Helper function for A2NLSF(..).
     * Polynomial evaluation.
     * @param p Polynomial, QPoly
     * @param x Evaluation point, Q12
     * @param dd  Order
     * @return return the polynomial evaluation, input QPoly
     */
    internal static int SKP_Silk_A2NLSF_eval_poly(    /* return the polynomial evaluation, input QPoly */
        int[]        p,    /* I    Polynomial, QPoly        */
        int    x,    /* I    Evaluation point, Q12    */
        int    dd    /* I    Order                    */
    )
    {
        int   n;
        int x_Q16, y32;

        y32 = p[ dd ];                                    /* QPoly */
        x_Q16 = x << 4;
        for( n = dd - 1; n >= 0; n-- )
        {
            y32 = SKP_SMLAWW( p[ n ], y32, x_Q16 );       /* QPoly */
        }
        return y32;
    }

    internal static void SKP_Silk_A2NLSF_init(
         int[]    a_Q16,
         int[]            P,
         int[]            Q,
         int        dd
    )
    {
        int k;

        /* Convert filter coefs to even and odd polynomials */
        P[dd] = 1 << QPoly;
        Q[dd] = 1 << QPoly;
        for( k = 0; k < dd; k++ )
        {
            if( QPoly < 16 )
            {
                P[ k ] = SigProcFIX.SKP_RSHIFT_ROUND( -a_Q16[ dd - k - 1 ] - a_Q16[ dd + k ], 16 - QPoly ); /* QPoly */
                Q[ k ] = SigProcFIX.SKP_RSHIFT_ROUND( -a_Q16[ dd - k - 1 ] + a_Q16[ dd + k ], 16 - QPoly ); /* QPoly */
            }
            else if( QPoly == 16 )
            {
                P[ k ] = -a_Q16[ dd - k - 1 ] - a_Q16[ dd + k ]; // QPoly
                Q[ k ] = -a_Q16[ dd - k - 1 ] + a_Q16[ dd + k ]; // QPoly
            }
            else
            {
                P[ k ] = ( -a_Q16[ dd - k - 1 ] - a_Q16[ dd + k ] ) << ( QPoly - 16 ); /* QPoly */
                Q[ k ] = ( -a_Q16[ dd - k - 1 ] + a_Q16[ dd + k ] ) << ( QPoly - 16 ); /* QPoly */
            }
        }

        /* Divide output zeros as we have that for even filter orders, */
        /* z =  1 is always a root input Q, and                        */
        /* z = -1 is always a root input P                             */
        for( k = dd; k > 0; k-- )
        {
            P[ k - 1 ] -= P[ k ];
            Q[ k - 1 ] += Q[ k ];
        }

        /* Transform polynomials from cos(n*f) to cos(f)^n */
        SKP_Silk_A2NLSF_trans_poly( P, dd );
        SKP_Silk_A2NLSF_trans_poly( Q, dd );
    }

    /**
     * Compute Normalized Line Spectral Frequencies (NLSFs) from whitening filter coefficients.
     * If not all roots are found, the a_Q16 coefficients are bandwidth expanded until convergence.
     * @param NLSF Normalized Line Spectral Frequencies, Q15 (0 - (2^15-1)), [d]
     * @param a_Q16 Monic whitening filter coefficients input Q16 [d]
     * @param d Filter order (must be even)
     */
    internal static void SKP_Silk_A2NLSF(
        int[]        NLSF,                 /* O    Normalized Line Spectral Frequencies, Q15 (0 - (2^15-1)), [d]    */
        int[]        a_Q16,                /* I/O  Monic whitening filter coefficients input Q16 [d]                   */
        int    d,                    /* I    Filter order (must be even)                                      */
        int[]  P,
        int[]  Q
    )
    {
        int      i, k, m, dd, root_ix, ffrac;
        int xlo, xhi, xmid;
        int ylo, yhi, ymid;
        int nom, den;
        int[] p;

        dd =  d >> 1;

        SKP_Silk_A2NLSF_init( a_Q16, P, Q, dd );

        /* Find roots, alternating between P and Q */
        p = P;    /* Pointer to polynomial */

        xlo = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ 0 ]; // Q12
        ylo = SKP_Silk_A2NLSF_eval_poly( p, xlo, dd );

        if( ylo < 0 )
        {
            /* Set the first NLSF to zero and move on to the next */
            NLSF[ 0 ] = 0;
            p = Q;                      /* Pointer to polynomial */
            ylo = SKP_Silk_A2NLSF_eval_poly( p, xlo, dd );
            root_ix = 1;                /* Index of current root */
        }
        else
        {
            root_ix = 0;                /* Index of current root */
        }
        k = 1;                          /* Loop counter */
        i = 0;                          /* Counter for bandwidth expansions applied */
        while( true )
        {
            /* Evaluate polynomial */
            if(OVERSAMPLE_COSINE_TABLE)
            {
                xhi = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[   k       >> 1 ] +
                  ( ( LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ ( k + 1 ) >> 1 ] -
                      LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[   k       >> 1 ] ) >> 1 );    /* Q12 */
            }
            else
            {
                xhi = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ k ]; /* Q12 */
            }

            yhi = SKP_Silk_A2NLSF_eval_poly( p, xhi, dd );

            /* Detect zero crossing */
            if( ( ylo <= 0 && yhi >= 0 ) || ( ylo >= 0 && yhi <= 0 ) )
            {
                /* Binary division */
                if(OVERSAMPLE_COSINE_TABLE)
                   ffrac = -128;
                else
                   ffrac = -256;

                for( m = 0; m < BIN_DIV_STEPS_A2NLSF_FIX; m++ )
                {
                    /* Evaluate polynomial */
                    xmid = SigProcFIX.SKP_RSHIFT_ROUND( xlo + xhi, 1 );
                    ymid = SKP_Silk_A2NLSF_eval_poly( p, xmid, dd );

                    /* Detect zero crossing */
                    if( ( ylo <= 0 && ymid >= 0 ) || ( ylo >= 0 && ymid <= 0 ) )
                    {
                        /* Reduce frequency */
                        xhi = xmid;
                        yhi = ymid;
                    }
                    else
                    {
                        /* Increase frequency */
                        xlo = xmid;
                        ylo = ymid;
                        if(OVERSAMPLE_COSINE_TABLE)
                            ffrac = ffrac + (64>>m);
                        else
                            ffrac = ffrac + (128>>m);
                    }
                }

                /* Interpolate */
                if (Math.Abs((long)ylo) < 65536)
                {
                    /* Avoid dividing by zero */
                    den = ylo - yhi;
                    nom = ( ylo << ( 8 - BIN_DIV_STEPS_A2NLSF_FIX ) ) + ( den >> 1 );
                    if( den != 0 )
                    {
                        ffrac += nom / den;
                    }
                }
                else
                {
                    /* No risk of dividing by zero because abs(ylo - yhi) >= abs(ylo) >= 65536 */
                    ffrac += ylo / ( ( ylo - yhi ) >> ( 8 - BIN_DIV_STEPS_A2NLSF_FIX ) );
                }
                if(OVERSAMPLE_COSINE_TABLE)
                    NLSF[ root_ix ] = Math.Min( ( k << 7 ) + ffrac, SKP_int16_MAX );
                else
                    NLSF[ root_ix ] = Math.Min( ( k << 8 ) + ffrac, SKP_int16_MAX );

                System.Diagnostics.Debug.Assert( NLSF[ root_ix ] >=     0 );
                System.Diagnostics.Debug.Assert( NLSF[ root_ix ] <= 32767 );

                root_ix++;        /* Next root */
                if( root_ix >= d )
                {
                    /* Found all roots */
                    break;
                }
                /* Alternate pointer to polynomial */
                p = (root_ix & 1) == 0 ? P : Q;

                /* Evaluate polynomial */
                if(OVERSAMPLE_COSINE_TABLE)
                    xlo = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ ( k - 1 ) >> 1 ] +
                      ( ( LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[   k       >> 1 ] -
                          LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ ( k - 1 ) >> 1 ] ) >> 1 ); // Q12
                else
                    xlo = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ k - 1 ]; // Q12

                ylo = ( 1 - ( root_ix & 2 ) ) << 12;
            }
            else
            {
                /* Increment loop counter */
                k++;
                xlo    = xhi;
                ylo    = yhi;

                bool b;

                if (OVERSAMPLE_COSINE_TABLE)
                    b = ( k > 2 * SigProcFIX.LSF_COS_TAB_SZ_FIX );
                else
                    b = ( k > SigProcFIX.LSF_COS_TAB_SZ_FIX );
                if (b)
                {
                    i++;
                    if( i > MAX_ITERATIONS_A2NLSF_FIX )
                    {
                        /* Set NLSFs to white spectrum and exit */
                        NLSF[ 0 ] = ( 1 << 15 ) / ( d + 1 );
                        for( k = 1; k < d; k++ )
                        {
                            NLSF[ k ] = SKP_SMULBB( k + 1, NLSF[ 0 ] );
                        }
                        return;
                    }

                    /* Error: Apply progressively more bandwidth expansion and run again */
                    Bwexpander32.SKP_Silk_bwexpander_32( a_Q16, d, 65536 - SKP_SMULBB( 10 + i, i ) ); // 10_Q16 = 0.00015

                    SKP_Silk_A2NLSF_init( a_Q16, P, Q, dd );
                    p = P;                            /* Pointer to polynomial */
                    xlo = LSFCosTable.SKP_Silk_LSFCosTab_FIX_Q12[ 0 ]; // Q12
                    ylo = SKP_Silk_A2NLSF_eval_poly( p, xlo, dd );
                    if( ylo < 0 )
                    {
                        /* Set the first NLSF to zero and move on to the next */
                        NLSF[ 0 ] = 0;
                        p = Q;                        /* Pointer to polynomial */
                        ylo = SKP_Silk_A2NLSF_eval_poly( p, xlo, dd );
                        root_ix = 1;                /* Index of current root */
                    }
                    else
                    {
                        root_ix = 0;                /* Index of current root */
                    }
                    k = 1;                            /* Reset loop counter */
                }
            }
        }
    }
}
