using UnityEngine;

namespace ProtoShape2D
{
	public class PS2DPlayerScript : MonoBehaviour
	{

		private float speed = 0.15f;

		Rigidbody2D rb;
		Collider2D col;

		private void Start()
		{
			rb = GetComponent<Rigidbody2D>();
			col = GetComponent<Collider2D>();
		}

		private void FixedUpdate()
		{
			var movement = (Input.GetAxis("Horizontal") * Vector3.right);
			transform.position += movement * speed;

			//Flip to right or left when changing direction
			if (movement.x < 0 && transform.localScale.x > 0)
			{
				transform.localScale =
					new Vector3(-transform.localScale.x, transform.localScale.y, transform.localScale.z);
			}
			else if (movement.x > 0 && transform.localScale.x < 0)
			{
				transform.localScale =
					new Vector3(-transform.localScale.x, transform.localScale.y, transform.localScale.z);
			}
		}

		private void Update()
		{
			//Jump
			if ((Input.GetKeyDown("w") || Input.GetKeyDown("up") || Input.GetKeyDown("space")) && IsGrounded)
			{
				rb.linearVelocity = Vector2.zero;
				rb.AddForce(Vector2.up * 450);
			}

			//Respawn
			if (transform.position.y < -80)
			{
				var newpos = new Vector3(-6.8f, 70, transform.position.z);
				var diff = transform.position - newpos;
				transform.position = newpos;
				Camera.main.transform.position -= diff;
			}
		}

		private bool IsGrounded
		{
			get
			{
				var results = new RaycastHit2D[5];
				return col.Cast(Vector2.down, results, 1f) > 0 ? true : false;
			}
		}

	}
}