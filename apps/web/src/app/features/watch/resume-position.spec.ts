import { resumePosition } from './resume-position';

describe('resumePosition', () => {
  const duration = 120;

  it('starts from the top when nothing is saved', () => {
    expect(resumePosition(null, duration)).toBe(0);
    expect(resumePosition(0, duration)).toBe(0);
  });

  it('resumes from a mid-video position', () => {
    expect(resumePosition(42.5, duration)).toBe(42.5);
  });

  it('starts over when the save is inside the final stretch', () => {
    expect(resumePosition(111, duration)).toBe(0);
    expect(resumePosition(120, duration)).toBe(0);
  });

  it('resumes right up to the edge of the final stretch', () => {
    expect(resumePosition(109.9, duration)).toBe(109.9);
  });
});
