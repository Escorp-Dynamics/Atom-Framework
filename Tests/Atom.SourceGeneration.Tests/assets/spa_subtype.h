enum spa_test_subtype {
	TEST_SUBTYPE_unknown,
	TEST_SUBTYPE_raw,
	TEST_SUBTYPE_START_Audio = 0x100, /* audio section */
	TEST_SUBTYPE_mp3,
	TEST_SUBTYPE_aac,	/** since 0.3.65 */
	TEST_SUBTYPE_control,	/**< control stream, data contains
				  *  some info. */
	TEST_SUBTYPE_S16 = TEST_SUBTYPE_mp3,
};
